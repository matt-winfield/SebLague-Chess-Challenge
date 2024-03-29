﻿using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;
using Board = ChessChallenge.API.Board;
using Move = ChessChallenge.API.Move;

public class BotV9_KeepLatestSearchResult_EvaluationNumbersTweaks : IChessBot
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

    // Dictionary of zobrist keys to evaluation scores (Item1), the depth the position was evaluated to (Item2), and the best move in that position (Item3)
    // Used to avoid re-evaluating the same position multiple times
    private readonly Dictionary<ulong, (int, int, Move)> _transpositionTable = new();
    private Move _bestMove;
    private bool searchAborted;

    public Move Think(Board board, Timer timer)
    {
        // Average 40 moves per game, so we start targetting 1/40th of the remaining time
        // Will move quicker as the game progresses / less time is remaining
        // Minimum 1/10 of the remaining time, to avoid timing out, divide by zero errors or negative numbers
        var cancellationTimer = new System.Timers.Timer(Math.Max(timer.MillisecondsRemaining / Math.Max(40 - board.PlyCount / 2, 10), 1));
        searchAborted = false;
        cancellationTimer.Elapsed += (s, e) =>
        {
            searchAborted = true;
            cancellationTimer.Stop();
        };
        cancellationTimer.Start();

        int score;
        var searchDepth = 1;
        _bestMove = Move.NullMove;
        do
        {
            // 9999999 and -9999999 are used as "infinity" values for alpha and beta
            score = Search(board, -9999999, 9999999, searchDepth, 0);
        } while (searchDepth++ < 20 && !searchAborted); // 20 is the max depth
        
        // This is for debugging purposes only, comment it out so it doesn't use up tokens!
        // The ttMemory calculation is storing 2 ints, so divide by 2 to get bytes, then divide by 1000000 to get MB
        // Console.WriteLine($"Current eval: {Evaluate(board)}, Best move score: {score}, Result: {_bestMove}, Depth: {searchDepth}, ttSize: {_transpositionTable.Count}, ttMemory: {(_transpositionTable.Count / 2d) / 1000000d}MB, fen: {board.GetFenString()}");
        
        return _bestMove;
    }
    
    private int Search(Board board, int alpha, int beta, int depthLeft, int plyFromRoot)
    {
        // If the current position has already been evaluated to at least the same depth, return the stored value
        if (_transpositionTable.TryGetValue(board.ZobristKey, out var value) && value.Item2 >= depthLeft)
        {
            if (plyFromRoot == 0)
                _bestMove = value.Item3;
            
            return value.Item1;
        }
        
        var legalMoves = GetOrderedLegalMoves(board, plyFromRoot);
        if (legalMoves.Length == 0)
        {
            // No legal moves + no check = stalemate
            if (!board.IsInCheck()) return 0;
            
            // No legal moves + check = checkmate
            // Ply depth is used to make the bot prefer checkmates that happen sooner
            // 9999998 is one less than "infinity" used as initial alpha/beta values, one less to avoid beta comparison failing
            return -9999998 + plyFromRoot;
        }
        
        if (depthLeft == 0)
            return Evaluate(board);

        var bestMoveInPosition = legalMoves[0];
        foreach (var move in legalMoves)
        {
            if (searchAborted) break;
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
            _bestMove = bestMoveInPosition;
        }

        _transpositionTable[board.ZobristKey] = (alpha, depthLeft, bestMoveInPosition);

        return alpha;
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
        // This is used in the case that the piece is a king
        var enemyKing = board.GetKingSquare(isWhite);
        // Encourage our king to move towards the enemy king, to "box it in"
        var distanceFromEnemyKing = Math.Abs(enemyKing.Rank - rank) + Math.Abs(enemyKing.File - file);
        // Encourage enemy king to move towards edge/corner
        var enemyKingDistanceFromCenter = (int) (Math.Abs(enemyKing.Rank - 3.5) + Math.Abs(enemyKing.File - 3.5));
        var boxInKingBonus = distanceFromEnemyKing + enemyKingDistanceFromCenter;
        
        // Using enum name uses more tokens than using the int value
        return (int)pieceType switch
        {
            1 => // Pawn
                (10 + (
                (board.GetPieceBitboard(PieceType.Pawn, !isWhite) & GetPassedPawnBitboard(rank, file, isWhite)) == 0 // Is passed pawn
                    ? 50 : 0)) // Add 50 if passed pawn
                // This position table encourages pawns to move forward with an emphasis on the center of the board
                // It places some importance on keeping pawns near the king to protect it
                 * GetSquareValueFromMultiBitboard(new[] { 0xffff0000000000, 0xffff3c0000, 0xff00ff3cc30000 },
                rank, file, isWhite),
            2 => // Knight
                // Encourage knights to move towards the center of the board
                10 * GetSquareValueFromMultiBitboard(new[] { 0x1818000000, 0x3c7e66667e3c00, 0x423c24243c4200 }, rank, file, isWhite),
            3 => // Bishop
                // Encourage bishops to position on long diagonals
                10 * GetSquareValueFromMultiBitboard(new[] { 0x24243c3c3c242400, 0x1818000000, 0x3c7e66667e3c00 }, rank, file, isWhite),
            4 => // Rook
                // Encourage rooks to position in the center-edge or cut off on second-last rank
                10 * GetSquareValueFromMultiBitboard(new[] { 0x7e000000000000, 0x8100000000003c }, rank, file, isWhite),
            5 => // Queen
                // Slightly encourage queen towards center of board
                10 * GetSquareValueFromMultiBitboard(new[] { 0x3c3c3c3e0400 }, rank, file, isWhite),
            6 => // King
                // Encourage king to take shelter behind pawns at start of the game
                (int)(10 * GetSquareValueFromMultiBitboard(new[] { 0xc3L, 0x66L }, rank, file, isWhite) *
                      (1 - endgameModifier))
                // Encourage king to "box in" enemy king at end of the game
                + (int)(10 * boxInKingBonus * endgameModifier),
            _ => 1
        };
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

    /**
     * Evaluate the current position. Score is from the perspective of the player to move, where positive is winning.
     */
    private int Evaluate(Board board)
    {
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