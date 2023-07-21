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

        foreach (PieceType pieceType in Enum.GetValues(typeof(PieceType)))
        {
            var pieceValue = values[(int)pieceType];
            var whiteBitboard = board.GetPieceBitboard(pieceType, true);
            var blackBitboard = board.GetPieceBitboard(pieceType, false);
            eval += pieceValue * (CountBits(whiteBitboard) - CountBits(blackBitboard));
        }

        return eval;
    }
    
    private int CountBits(ulong bitboard)
    {
        var count = 0;
        while (bitboard != 0)
        {
            // Subtracting 1 from a binary number will always flip the rightmost 1 to 0 and all 0s to 1s to the right of it
            // So if we AND the original number with the number minus 1, we will get a number with the rightmost 1 flipped to 0
            // We can then repeat this process until the number is 0, counting the number of times we can do this
            // 10010100 & 10010011 = 10010000
            // 10010000 & 10001111 = 10000000
            // 10000000 & 01111111 = 00000000
            bitboard &= bitboard - 1;
            count++;
        }

        return count;
    }
}