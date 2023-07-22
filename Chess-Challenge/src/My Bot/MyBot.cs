using System;
using System.Collections.Generic;
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

    private static int positiveInfinity = 99999999;
    private static int negativeInfinity = -positiveInfinity;

    public Move Think(Board board, Timer timer)
    {
        var (score, move) = Search(board, negativeInfinity, positiveInfinity, 5);
        Console.WriteLine($"Current eval: {Evaluate(board, board.GetLegalMoves())}, Best move score: {score}, fen: {board.GetFenString()}");
        return move;
    }
    
    private (int, Move) Search(Board board, int alpha, int beta, int depthLeft)
    {
        var legalMoves = board.GetLegalMoves();
        var bestMove = legalMoves.Length > 0 ? legalMoves[0] : new Move();
        if (depthLeft == 0 || legalMoves.Length == 0) return (Evaluate(board, legalMoves) * (board.IsWhiteToMove ? 1 : -1), bestMove);
        foreach (var move in legalMoves)
        {
            board.MakeMove(move);
            var (score, _) = Search(board, -beta, -alpha, depthLeft - 1);
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

    private static double GetPawnPositionalMultiplier(bool isWhite, int rank, int file)
    {
        return 1 + (
            isWhite
                ? (7 - rank) / 7
                : rank / 7
            );
    }
    
    private static double GetCenterPositionalMultiplier(bool isWhite, int rank, int file)
    {
        return 1 + (3.5 - Math.Min(Math.Abs(rank - 3.5), Math.Abs(file - 3.5))) / 3.5;
    }

    private Dictionary<PieceType, Func<bool, int, int, double>> positionalMultipliers =
        new()
        {
            { PieceType.Pawn, GetPawnPositionalMultiplier },
            { PieceType.Knight, GetCenterPositionalMultiplier },
            { PieceType.Queen, GetCenterPositionalMultiplier }
        };

    private int Evaluate(Board board, Move[] legalMoves)
    {
        if (legalMoves.Length == 0)
        {
            // No legal moves + check = checkmate
            if (board.IsInCheck())
            {
                return board.IsWhiteToMove ? Int32.MinValue : Int32.MaxValue;
            }

            // No legal moves + no check = stalemate
            return 0;
        }

        var eval = 0;

        foreach (var pieceList in board.GetAllPieceLists())
        {
            var pieceType = pieceList.TypeOfPieceInList;
            if (pieceType == PieceType.None) continue;
            foreach (var piece in pieceList)
            {
                var positionalMultiplier = positionalMultipliers.TryGetValue(pieceType, out var multiplierFunc)
                    ? multiplierFunc(pieceList.IsWhitePieceList, piece.Square.Rank, piece.Square.File)
                    : 1;
                eval += (int)(values[(int)piece.PieceType] * positionalMultiplier) * (piece.IsWhite ? 1 : -1);
            }
        }

        return eval;
    }
}