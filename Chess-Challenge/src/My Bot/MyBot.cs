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
    
    public Move Think(Board board, Timer timer)
    {
        var (score, move) = board.IsWhiteToMove ?
            AlphaBetaMax(board, Int32.MinValue, Int32.MaxValue, 5)
            : AlphaBetaMin(board, Int32.MinValue, Int32.MaxValue, 5);
        Console.WriteLine($"Current eval: {Evaluate(board)}, Best move score: {score}");
        return move;
    }

    private (int, Move) AlphaBetaMax(Board board, int lowerBound, int upperBound, int depthLeft)
    {
        int score;
        var legalMoves = board.GetLegalMoves();
        Move bestMove = new Move();
        if (depthLeft == 0) return (Evaluate(board), bestMove);
        foreach (var move in legalMoves)
        {
            board.MakeMove(move);
            (score, _) = AlphaBetaMin(board, lowerBound, upperBound, depthLeft - 1);
            board.UndoMove(move);
            if (score >= upperBound)
            {
                return (upperBound, bestMove); 
            }
            if (score > lowerBound)
            {
                lowerBound = score;
                bestMove = move;
            }
        }

        return (lowerBound, bestMove);
    }
    
    private (int, Move) AlphaBetaMin(Board board, int lowerBound, int upperBound, int depthLeft)
    {
        int score;
        var legalMoves = board.GetLegalMoves();
        Move bestMove = new Move();
        if (depthLeft == 0) return (Evaluate(board), bestMove);
        foreach (var move in legalMoves)
        {
            board.MakeMove(move);
            (score, _) = AlphaBetaMax(board, lowerBound, upperBound, depthLeft - 1);
            board.UndoMove(move);
            if (score <= lowerBound)
            {
                return (lowerBound, bestMove); 
            }
            if (score < upperBound)
            {
                upperBound = score;
                bestMove = move;
            }
        }

        return (upperBound, bestMove);
    }
    
    private int Evaluate(Board board)
    {
        var eval = 0;

        foreach (var piece in GetPieces(board))
        {
            // x1 near edges, x1.5 near center
            // Index 3/4 is center, 0/7 is edge
            var rankDistanceFromCenter = Math.Abs(piece.Square.Rank - 3);
            eval += values[(int)piece.PieceType] * (piece.IsWhite ? 1 : -1);
        }

        return eval;
    }
    
    private Piece[] GetPieces(Board board)
    {
        var pieces = new List<Piece>();
        for (int i = 0; i < 64; i++)
        {
            var piece = board.GetPiece(new Square(i));
            if (!piece.IsNull)
            {
                pieces.Add(piece);
            }
        }

        return pieces.ToArray();
    }
}