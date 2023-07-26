using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;
using Board = ChessChallenge.API.Board;
using Move = ChessChallenge.API.Move;

public class MyBot : IChessBot
{
    private int[] values = 
    {
        0, // None
        100, // Pawn
        350, // Knight
        350, // Bishop
        525, // Rook
        1000, // Queen
        200 // King - This value is used for positional multiplier
    };

    // Dictionary of zobrist keys to evaluation scores (Item1), the depth the position was evaluated to (Item2), and the best move in that position (Item3)
    // Used to avoid re-evaluating the same position multiple times
    private readonly Dictionary<ulong, (int, int, Move)> _transpositionTable = new();
    
    private Move _bestMove;
    private bool _searchAborted;
    // The time remaining in milliseconds when the search should stop
    private int _timeRemainingToStopSearch;
    private Timer _timer;

    private ulong[][] _positionalMultipliers =
    {
        new ulong[0],
        new[] { 0x2b000000000000ul, 0x547c1800406000ul, 0x36326618000000ul }, // Pawn
        new[] { 0x200000000000ul, 0x3c5e3c60700000ul, 0x24587a1e0c2000ul }, // Knight
        new[] { 0x207c3800000000ul, 0x72020418606000ul, 0x10224876be4600ul }, // Bishop
        new[] { 0x10000000000000ul, 0x9aac682000000000ul, 0x556fd61800000018ul, 0x6d63240c10000024ul }, // Rook
        new[] { 0xf0a0e00000000000ul, 0x1460b00000002000ul, 0x8cc0500000001000ul }, // Queen
        new[] { 0xc2ul, 0xc056ul } // King
    };

    private ulong[][] _endgamePositionalMultipliers =
    {
        new ulong[0],
        new[] { 0xffc30000000000ul, 0x3c8300000000ul, 0xff3c4503005f00ul }, // Pawn
        new[] { 0x1c08000000ul, 0x46034180000ul }, // Knight
        new[] { 0x8000000ul, 0x203e341c0000ul }, // Bishop
        new[] { 0xff4f0f0406000000 }, // Rook
        new[] { 0x1838f848000000ul, 0xbe764056b6240000ul, 0x58649e083ad80000ul }, // Queen
        new[] { 0x20600000000000ul, 0x5a1e7e3c180000ul, 0x2084a17850641800ul } // King
    };

    public Move Think(Board board, Timer timer)
    {
        // Average 40 moves per game, so we start targetting 1/40th of the remaining time
        // Will move quicker as the game progresses / less time is remaining
        // Minimum 1/10 of the remaining time, to avoid timing out, divide by zero errors or negative numbers
        _timeRemainingToStopSearch = timer.MillisecondsRemaining - timer.MillisecondsRemaining / Math.Max(40 - board.PlyCount / 2, 10);
        _searchAborted = false;
        _timer = timer;

        int score;
        var searchDepth = 1;
        _bestMove = Move.NullMove;
        do
        {
            // 9999999 and -9999999 are used as "infinity" values for alpha and beta
            score = Search(board, -9999999, 9999999, searchDepth, 0);
        } while (searchDepth++ < 20 && !_searchAborted); // 20 is the max depth
        
        // This is for debugging purposes only, comment it out so it doesn't use up tokens!
        // The ttMemory calculation is storing 2 ints, so divide by 2 to get bytes, then divide by 1000000 to get MB
        // Console.WriteLine($"Current eval: {Evaluate(board)}, Best move score: {score}, Result: {_bestMove}, Depth: {searchDepth}, time remaining: {timer.MillisecondsRemaining}, ttSize: {_transpositionTable.Count}, ttMemory: {(_transpositionTable.Count / 2d) / 1000000d}MB, fen: {board.GetFenString()}");
        
        return _bestMove;
    }
    
    // If depthLeft is 0 (or less), perform a quintescence search
    // https://www.chessprogramming.org/Quiescence_Search
    private int Search(Board board, int alpha, int beta, int depthLeft, int plyFromRoot)
    {
        var qsearch = depthLeft < 0;
        if (_timer.MillisecondsRemaining <= _timeRemainingToStopSearch)
        {
            _searchAborted = true;
        }
        
        // If the current position has already been evaluated to at least the same depth, return the stored value
        if (_transpositionTable.TryGetValue(board.ZobristKey, out var value) && value.Item2 >= depthLeft)
        {
            if (plyFromRoot == 0)
                // Get the saved best move in this position
                _bestMove = value.Item3;
            else
                // Return the saved evaluation score
                return value.Item1;
        }

        // Repetition is a draw, so return 0
        if (board.IsRepeatedPosition())
            return 0;
        
        // Get the legal moves, if it's a quintescence search only get captures
        var legalMoves = GetOrderedLegalMoves(board, plyFromRoot, qsearch);
        if (!qsearch && legalMoves.Length == 0)
        {
            // No legal moves + no check = stalemate
            if (!board.IsInCheck()) return 0;
            
            // No legal moves + check = checkmate
            // Ply depth is used to make the bot prefer checkmates that happen sooner
            // 9999998 is one less than "infinity" used as initial alpha/beta values, one less to avoid beta comparison failing
            return -9999998 + plyFromRoot;
        }

        // Quiescence search
        if (qsearch)
        {
            var staticEval = Evaluate(board);
            if (staticEval >= beta) return beta; // This position is better than beta, so the opponent will not allow it to happen
            alpha = Math.Max(alpha, staticEval); // This position is a better move
        }

        var bestMoveInPosition = Move.NullMove;
        foreach (var move in legalMoves)
        {
            if (_searchAborted) break;
            board.MakeMove(move);
            var eval = -Search(board, -beta, -alpha, depthLeft - 1, plyFromRoot + 1);
            board.UndoMove(move);
            if (eval >= beta)
                // Opponent will not allow this move to happen because it's too good
                return beta;

            // New best move found
            if (eval > alpha)
            {
                alpha = eval;
                bestMoveInPosition = move;
            }
        }

        if (plyFromRoot == 0)
        {
            // If no best move was found (e.g. search cancelled), just use the first legal move
            _bestMove = bestMoveInPosition.IsNull ? legalMoves[0] : bestMoveInPosition;
        }

        if (!qsearch)
            _transpositionTable[board.ZobristKey] = (alpha, depthLeft, bestMoveInPosition);

        return alpha;
    }
    
    private Move[] GetOrderedLegalMoves(Board board, int plyFromRoot, bool capturesOnly = false)
    {
        return board.GetLegalMoves(capturesOnly).OrderByDescending(move =>
        {
            // Always start with the best move from the previous iteration
            if (plyFromRoot == 0 && _bestMove.Equals(move))
                return 9999999;
            
            var score = 0;
            
            // Prioritise moves that capture pieces
            if (move.CapturePieceType != PieceType.None)
            {
                score += values[(int)move.CapturePieceType];
            }

            // Prioritise moves that promote pawns
            if (move.IsPromotion)
            {
                score += values[(int)move.PromotionPieceType];
            }

            return score;
        }).ToArray();
    }

    private ulong GetPassedPawnBitboard(int rank, int file, bool isWhite) =>
            (isWhite // Forward mask
                ? ulong.MaxValue << 8 * (rank + 1) // White, going up the board
                : ulong.MaxValue >> 8 * (8 - rank)) // Black, going down the board
            &
                (0x0101010101010101UL << file // File mask
               | 0x0101010101010101UL << Math.Max(0, file - 1) // Left file mask
               | 0x0101010101010101UL << Math.Min(7, file + 1)); // Right file mask

    /**
     * Use the concept of a "multi-bitboard" to store values for each square on the board.
     * Use https://multibitsboard-generator.vercel.app/ to generate the bitboards.
     * Each bitboard is a 64-bit integer, with each bit representing a square on the board.
     * Read the value of each bitboard at the square index, and combine them to get the binary representation of the value.
     * 
     * This approach usually wouldn't be used for chess programming, as it's harder to work with and has a slightly higher
     * computational cost for reading values than simply using a multi-dimensional array (not a significant amount, but still higher).
     * However we need to minimise number of tokens used, and this approach uses significantly fewer.
     */
    private int GetSquareValueFromMultiBitboard(ulong[] bitboards, int rank, int file, bool isWhite)
    {
        var correctedRank = isWhite ? rank : 7 - rank;
        return Convert.ToInt32(
            bitboards.Select(bitboard => ((bitboard >> (correctedRank * 8 + file)) & 1) != 0 ? "1" : "0")
                .Aggregate((a, b) => a + b),
            2
        );
    }
    
    private int GetPiecePositionalBonus(PieceType pieceType, Board board, bool isWhite, int rank, int file, double endgameModifier)
    {
        return (int) (
            10 * endgameModifier * GetSquareValueFromMultiBitboard(_positionalMultipliers[(int)pieceType], rank, file, isWhite)
            + 10 * (1 - endgameModifier) * GetSquareValueFromMultiBitboard(_endgamePositionalMultipliers[(int) pieceType], rank, file, isWhite)
        );
    }

    /**
     * Evaluate the current position. Score is from the perspective of the player to move, where positive is winning.
     */
    private int Evaluate(Board board)
    {
        // Endgame modifier is a linear function that goes from 0 to 1 as piecesRemaining goes from 32 to 12 (i.e. the endgame)
        // Use to encourage the bot to act differently in the endgame
        // 28 remaining pieces = 0; 12 remaining pieces = 1;
        // x = 32, y = 0
        // x = 12, y = 1
        // y = mx + c
        // 0 = 32m + c, 1 = 12m + c
        // c = -32m
        // 1 = 12m - 32m
        // 1 = -20m
        // m = -1/20
        // c = 32/20
        // y (endgame modifier) = -x/20 + 32/20, where x = piecesRemaining
        // Clamp between 0 and 1
        var endgameModifier = Math.Max(Math.Min(-CountBits(board.AllPiecesBitboard)/20d + 32/20d, 0), 1);
        
        int eval = 0;
        foreach (var pieceList in board.GetAllPieceLists())
        {
            var pieceType = pieceList.TypeOfPieceInList;
            if (pieceType == PieceType.None) continue;
            foreach (var piece in pieceList)
            {
                var positionalBonus = GetPiecePositionalBonus(pieceType, board, piece.IsWhite,
                    piece.Square.Rank, piece.Square.File, endgameModifier);
                eval += (values[(int)piece.PieceType] + positionalBonus) * (piece.IsWhite ? 1 : -1);
            }
        }

        return eval * (board.IsWhiteToMove ? 1 : -1);
    }
    
    private int CountBits(ulong n)
    {
        int count = 0;
        while (n != 0)
        {
            // n - 1 will flip the rightmost 1 bit to 0 and all the bits to the right of it to 1
            // e.g. 1000 -> 0111
            // n & (n - 1) will therefore set the rightmost 1 bit, and all following bits, to 0
            // If we keep doing this until the number is zero, the number of iterations will be the number of 1 bits
            n &= n - 1;
            count++;
        }
        return count;
    }
}