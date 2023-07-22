using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;

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
        10000 // King
    };

    private static int positiveInfinity = 9999999;
    private static int negativeInfinity = -positiveInfinity;
    private static int mateScore = positiveInfinity - 1;

    public Move Think(Board board, Timer timer)
    {
        var (score, move) = Search(board, negativeInfinity, positiveInfinity, 5);
        Console.WriteLine($"Current eval: {Evaluate(board, board.GetLegalMoves())}, Best move score: {score}, Result: {move}, ttSize: {transpositionTable.Count}, fen: {board.GetFenString()}");
        return move;
    }
    
    private (int, Move) Search(Board board, int alpha, int beta, int depthLeft, int? initialDepth = null)
    {
        initialDepth = initialDepth ?? depthLeft;
        var plyDepth = initialDepth.Value - depthLeft;
        var legalMoves = GetOrderedLegalMoves(board);
        var bestMove = legalMoves.Length > 0 ? legalMoves[0] : new Move();
        if (depthLeft == 0 || legalMoves.Length == 0) return (Evaluate(board, legalMoves, plyDepth) * (board.IsWhiteToMove ? 1 : -1), bestMove);
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

            if (score > alpha)
            {
                alpha = score;
                bestMove = move;
            }
        }

        return (alpha, bestMove);
    }
    
    private static double GetPawnPositionalMultiplier(Board board, bool isWhite, int rank, int file, double endgameModifier)
    {
        return 1 + (
            board.IsWhiteToMove
                ? (7 - rank) / 7
                : rank / 7
            ) * 2 + 4 * endgameModifier;
    }

    private static double GetKingPositionalMultiplier(Board board, bool isWhite, int rank, int file, double endgameModifier)
    {
        double extraWeighting = 0;
        var enemyKing = board.GetKingSquare(isWhite);
        
        // Encourage our king to move towards the enemy king, to "box it in"
        var distanceFromEnemyKing = Math.Abs(enemyKing.Rank - rank) + Math.Abs(enemyKing.File - file);
        extraWeighting += (14 - distanceFromEnemyKing) / 14d;
        
        // Encourage enemy king to move towards edge/corner
        var distanceFromCenter = Math.Abs(enemyKing.Rank - 3.5) + Math.Abs(enemyKing.File - 3.5);
        extraWeighting += (3.5 - distanceFromCenter) / 3.5;

        return 1 + (extraWeighting * endgameModifier * endgameModifier);
    }
    
    private static double GetCenterPositionalMultiplier(Board board, bool isWhite, int rank, int file, double endgameModifier)
    {
        return 1 + (3.5 - Math.Min(Math.Abs(rank - 3.5), Math.Abs(file - 3.5))) / 3.5;
    }

    private Dictionary<PieceType, Func<Board, bool, int, int, double, double>> positionalMultipliers =
        new()
        {
            { PieceType.Pawn, GetPawnPositionalMultiplier },
            { PieceType.Knight, GetCenterPositionalMultiplier },
            { PieceType.Queen, GetCenterPositionalMultiplier },
            { PieceType.King, GetKingPositionalMultiplier }
        };
    
    private Dictionary<ulong, int> transpositionTable = new();

    private Move[] GetOrderedLegalMoves(Board board, bool capturesOnly = false)
    {
        var legalMoves = board.GetLegalMoves(capturesOnly);
        return legalMoves.OrderByDescending((move) =>
        {
            var score = 0;
            if (move.CapturePieceType != PieceType.None)
            {
                score += values[(int)move.CapturePieceType];
            }

            if (move.IsPromotion)
            {
                score += values[(int)move.CapturePieceType];
            }

            return score;
        }).ToArray();
    }

    private int Evaluate(Board board, Move[] legalMoves, int plyDepth = 0)
    {
        var ttKey = board.ZobristKey;
        
        var eval = 0;
        if (legalMoves.Length == 0)
        {
            // No legal moves + check = checkmate
            if (board.IsInCheck())
            {
                // Ply depth is used to make the bot prefer checkmates that happen sooner
                eval = (mateScore - plyDepth) * (board.IsWhiteToMove ? -1 : 1);
            }
            else
            {
                // No legal moves + no check = stalemate
                return 0;
            }

            transpositionTable.TryAdd(ttKey, eval);
            return eval;
        }
        
        if (transpositionTable.ContainsKey(ttKey))
        {
            return transpositionTable[ttKey];
        }

        var pieceLists = board.GetAllPieceLists();
        var piecesRemaining = CountBits(board.AllPiecesBitboard);
        
        // Endgame modifier is a linear function that goes from 0 to 1 as piecesRemaining goes from 32 to 0 
        // Use to encourage the bot to act differently in the endgame
        var endgameModifier = (32 - piecesRemaining) / 32d;
        
        foreach (var pieceList in pieceLists)
        {
            var pieceType = pieceList.TypeOfPieceInList;
            if (pieceType == PieceType.None) continue;
            foreach (var piece in pieceList)
            {
                var positionalMultiplier = positionalMultipliers.TryGetValue(pieceType, out var multiplierFunc)
                    ? multiplierFunc(board, piece.IsWhite, piece.Square.Rank, piece.Square.File, endgameModifier)
                    : 1;
                eval += (int)(values[(int)piece.PieceType] * positionalMultiplier) * (piece.IsWhite ? 1 : -1);
            }
        }

        transpositionTable.Add(ttKey, eval);
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
            n &= (n - 1);
            count++;
        }
        return count;
    }
}