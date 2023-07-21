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
        Console.WriteLine($"Current eval: {Evaluate(board)}, Best move score: {score}, fen: {board.GetFenString()}");
        return move;
    }

    private (int, Move) AlphaBetaMax(Board board, int lowerBound, int upperBound, int depthLeft)
    {
        int score;
        var legalMoves = board.GetLegalMoves();
        Move bestMove = legalMoves.Length > 0 ? legalMoves[0] : new Move();
        if (depthLeft == 0 || legalMoves.Length == 0) return (Evaluate(board), bestMove);
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
        Move bestMove = legalMoves.Length > 0 ? legalMoves[0] : new Move();
        if (depthLeft == 0 || legalMoves.Length == 0) return (Evaluate(board), bestMove);
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

    private int Evaluate(Board board)
    {
        var eval = 0;
        
        if (board.IsInCheckmate())
        {
            return board.IsWhiteToMove ? Int32.MinValue : Int32.MaxValue;
        }

        if (board.IsDraw())
        {
            return eval;
        }

        foreach (PieceType pieceType in Enum.GetValues(typeof(PieceType)))
        {
            if (pieceType == PieceType.None) continue;
            foreach (var isWhite in new[] {true, false})
            {
                var pieceList = board.GetPieceList(pieceType, isWhite);
                foreach (var piece in pieceList)
                {
                    var positionalMultiplier = positionalMultipliers.TryGetValue(pieceType, out var multiplierFunc)
                        ? multiplierFunc(isWhite, piece.Square.Rank, piece.Square.File)
                        : 1;
                    eval += (int)(values[(int)piece.PieceType] * positionalMultiplier) * (piece.IsWhite ? 1 : -1);
                }
            }
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