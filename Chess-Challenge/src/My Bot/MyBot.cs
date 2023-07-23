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
        200 // King - This value is used for the endgame, where the king is encouraged to move towards the enemy king
    };

    // Dictionary of zobrist keys to evaluation scores, to avoid re-evaluating the same position multiple times
    private readonly Dictionary<ulong, int> _transpositionTable = new();
    private bool searchAborted;

    public Move Think(Board board, Timer timer)
    {
        // Average 40 moves per game, so we target 1/40th of the remaining time
        // Will move quicker as the game progresses / less time is remaining
        var cancellationTimer = new System.Timers.Timer(timer.MillisecondsRemaining / 40);
        searchAborted = false;
        cancellationTimer.Elapsed += (s, e) =>
        {
            searchAborted = true;
            cancellationTimer.Stop();
        };
        cancellationTimer.Start();

        var (score, move) = (0, new Move());
        var searchDepth = 1;
        do
        {
            // 9999999 and -9999999 are used as "infinity" values for alpha and beta
            var result = Search(board, -9999999, 9999999, searchDepth);
            if (!searchAborted) (score, move) = result;
        } while (searchDepth++ < 20 && !searchAborted); // 20 is the max depth
        
        // This is for debugging purposes only, comment it out so it doesn't use up tokens!
        // The ttMemory calculation is storing ints, so divide by 4 to get bytes, then divide by 1000000 to get MB
        // Console.WriteLine($"Current eval: {Evaluate(board, board.GetLegalMoves())}, Best move score: {score}, Result: {move}, Depth: {searchDepth}, ttSize: {_transpositionTable.Count}, ttMemory: {(_transpositionTable.Count / 4d) / 1000000d}MB, fen: {board.GetFenString()}");
        
        return move;
    }
    
    private (int, Move) Search(Board board, int alpha, int beta, int depthLeft, int? initialDepth = null)
    {
        if (searchAborted) return (0, new Move());
        
        initialDepth ??= depthLeft;
        var legalMoves = GetOrderedLegalMoves(board);
        var bestMove = legalMoves.FirstOrDefault(new Move());
        if (depthLeft == 0 || legalMoves.Length == 0) return (Evaluate(board, legalMoves, initialDepth.Value - depthLeft) * (board.IsWhiteToMove ? 1 : -1), bestMove);
        foreach (var move in legalMoves)
        {
            board.MakeMove(move);
            var (score, _) = Search(board, -beta, -alpha, depthLeft - 1, initialDepth);
            score = -score;
            board.UndoMove(move);
            if (score >= beta)
            {
                // Opponent will not allow this move to happen because it's too good
                return (beta, bestMove);
            }

            if (score <= alpha) continue;
            alpha = score;
            bestMove = move;
        }

        return (alpha, bestMove);
    }

    private ulong GetPassedPawnBitboard(int rank, int file, bool isWhite) =>
            (isWhite // Forward mask
                ? ulong.MaxValue << 8 * (rank + 1) // White, going up the board
                : ulong.MaxValue >> 8 * (8 - rank)) // Black, going down the board
            &
                (0x0101010101010101u << file // File mask
               | 0x0101010101010101u << Math.Max(0, file - 1) // Left file mask
               | 0x0101010101010101u << Math.Min(7, file + 1)); // Right file mask

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
    private int GetSquareValueFromMultiBitboard(long[] bitboards, int rank, int file, bool isWhite)
    {
        var correctedRank = isWhite ? rank : 7 - rank;
        var binary = bitboards.Select(bitboard => ((bitboard >> (correctedRank * 8 + file)) & 1) != 0 ? "1" : "0").Aggregate((a, b) => a + b);
        return Convert.ToInt32(binary, 2);
    }
    
    private int GetPiecePositionalBonus(PieceType pieceType, Board board, bool isWhite, int rank, int file, double endgameModifier)
    {
        switch ((int)pieceType) // Using enum name uses more tokens than using the int value
        {
            case 1: // Pawn
                var isPassed = (board.GetPieceBitboard(PieceType.Pawn, !isWhite) &
                               GetPassedPawnBitboard(rank, file, isWhite)) == 0;
                // This position table encourages pawns to move forward with an emphasis on the center of the board
                // It places some importance on keeping pawns near the king to protect it
                return (50 + (isPassed ? 200 : 0)) * GetSquareValueFromMultiBitboard(new[] { 0xffff0000000000, 0xffff3c0000, 0xff00ff3cc30000 },
                    rank, file, isWhite);
            case 2: // Knight
                // Encourage knights to move towards the center of the board
                return 50 * GetSquareValueFromMultiBitboard(new[] { 0x1818000000, 0x3c7e66667e3c00, 0x423c24243c4200 }, rank, file, isWhite);
            case 3: // Bishop
                // Encourage bishops to position on long diagonals
                return 50 * GetSquareValueFromMultiBitboard(new[] { 0x24243c3c3c242400, 0x1818000000, 0x3c7e66667e3c00 }, rank, file, isWhite);
            case 4: // Rook
                // Encourage rooks to position in the center-edge or cut off on second-last rank
                return 50 * GetSquareValueFromMultiBitboard(new[] { 0x7e000000000000, 0x81000000000018 }, rank, file, isWhite);
            case 5: // Queen
                // Slightly encourage queen towards center of board
                return 50 * GetSquareValueFromMultiBitboard(new[] { 0x3c3c3c3e0400 }, rank, file, isWhite);
            case 6: // King
                var enemyKing = board.GetKingSquare(isWhite);

                // Encourage our king to move towards the enemy king, to "box it in"
                var distanceFromEnemyKing = Math.Abs(enemyKing.Rank - rank) + Math.Abs(enemyKing.File - file);

                // Encourage enemy king to move towards edge/corner
                var distanceFromCenter = Math.Abs(enemyKing.Rank - 3.5) + Math.Abs(enemyKing.File - 3.5);

                return (int)(((int)(100 * ((14 - distanceFromEnemyKing) / 14d)) +
                              (int)(100 * ((3.5 - distanceFromCenter) / 3.5d))) * endgameModifier);
        }

        return 1;
    }


    private Move[] GetOrderedLegalMoves(Board board, bool capturesOnly = false)
    {
        return board.GetLegalMoves(capturesOnly).OrderByDescending(move =>
        {
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

            board.MakeMove(move);
            score += _transpositionTable.GetValueOrDefault(board.ZobristKey, 0) * (board.IsWhiteToMove ? -1 : 1);
            board.UndoMove(move);

            return score;
        }).ToArray();
    }

    private int Evaluate(Board board, Move[] legalMoves, int plyDepth = 0)
    {
        var ttKey = board.ZobristKey;
        
        int eval = 0;
        if (legalMoves.Length == 0)
        {
            // No legal moves + no check = stalemate
            if (!board.IsInCheck()) return 0;
            
            // No legal moves + check = checkmate
            // Ply depth is used to make the bot prefer checkmates that happen sooner
            // 9999998 is one less than "infinity" used as initial alpha/beta values, one less to avoid beta comparison failing
            eval = (9999998 - plyDepth) * (board.IsWhiteToMove ? -1 : 1);
            _transpositionTable[ttKey] = eval;
            return eval;
        }
        
        if (_transpositionTable.ContainsKey(ttKey))
        {
            return _transpositionTable[ttKey];
        }
        
        // Endgame modifier is a linear function that goes from 0 to 1 as piecesRemaining goes from 28 to 12 (i.e. the endgame)
        // Use to encourage the bot to act differently in the endgame
        // 28 remaining pieces = 0; 12 remaining pieces = 1;
        // x = 28, y = 0
        // x = 12, y = 1
        // y = mx + c
        // 0 = 28m + c, 1 = 12m + c
        // c = -28m
        // 1 = 12m - 28m
        // 1 = -16m
        // m = -1/16
        // c = 28/16
        // y (endgame modifier) = -x/16 + 28/16, where x = piecesRemaining
        // Clamp between 0 and 1
        var endgameModifier = Math.Max(Math.Min(-CountBits(board.AllPiecesBitboard)/20d + 32/20d, 0), 1);
        
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

        _transpositionTable[ttKey] = eval;
        return eval;
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