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
        var (score, move) = board.IsWhiteToMove ?
            AlphaBetaMax(board, Int32.MinValue, Int32.MaxValue, 2)
            : AlphaBetaMin(board, Int32.MinValue, Int32.MaxValue, 2);
        Console.WriteLine($"Current eval: {Evaluate(board)}, Best move score: {score}");
        return move;
    }

    private (int, Move) AlphaBetaMax(Board board, int lowerBound, int upperBound, int depthLeft)
    {
        int score;
        var legalMoves = board.GetLegalMoves();
        Move bestMove = legalMoves[0];
        if (depthLeft == 0) return (-Evaluate(board), bestMove);
        foreach (var move in legalMoves)
        {
            board.MakeMove(move);
            (score, _) = AlphaBetaMin(board, lowerBound, upperBound, depthLeft - 1);
            Console.WriteLine($"Trying move {move}, depth {depthLeft}, state {board.GetFenString()}, score {score}");
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
        Move bestMove = legalMoves[0];
        if (depthLeft == 0) return (Evaluate(board), bestMove);
        foreach (var move in legalMoves)
        {
            board.MakeMove(move);
            (score, _) = AlphaBetaMax(board, lowerBound, upperBound, depthLeft - 1);
            Console.WriteLine($"Trying move {move}, depth {depthLeft}, state {board.GetFenString()}, score {score}");
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