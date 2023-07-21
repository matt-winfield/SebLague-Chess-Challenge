using System;
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
        var (score, move) = alphaBetaMax(board, Int32.MinValue, Int32.MaxValue, 5);
        return move ?? board.GetLegalMoves()[0];
    }

    private (int, Move?) alphaBetaMax(Board board, int alpha, int beta, int depthLeft)
    {
        int score;
        if (depthLeft == 0) return (Evaluate(board), null);
        Move? bestMove = null;
        foreach (var move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            (score, _) = alphaBetaMin(board, alpha, beta, depthLeft - 1);
            board.UndoMove(move);
            if (score >= beta)
            {
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
    
    private (int, Move?) alphaBetaMin(Board board, int alpha, int beta, int depthLeft)
    {
        int score;
        if (depthLeft == 0) return (-Evaluate(board), null);
        Move? bestMove = null;
        foreach (var move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            (score, _) = alphaBetaMax(board, alpha, beta, depthLeft - 1);
            board.UndoMove(move);
            if (score <= alpha)
            {
                return (alpha, bestMove); 
            }
            if (score < beta)
            {
                beta = score;
                bestMove = move;
            }
        }

        return (beta, bestMove);
    }
    
    private int Evaluate(Board board)
    {
        var pieceLists = board.GetAllPieceLists();
        var eval = 0;
        
        foreach (var pieceList in pieceLists)
        {
            foreach (var piece in pieceList)
            {
                eval += (piece.IsWhite ? 1 : -1) * values[(int)piece.PieceType];
            }
        }

        return eval;
    }
}