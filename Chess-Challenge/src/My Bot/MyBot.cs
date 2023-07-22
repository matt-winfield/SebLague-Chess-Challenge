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
        200 // King - This value is used for the endgame, where the king is encouraged to move towards the enemy king
    };

    private static int positiveInfinity = 9999999;
    private static int negativeInfinity = -positiveInfinity;
    private static int mateScore = positiveInfinity - 1;
    private bool searchAborted = false;

    public Move Think(Board board, Timer timer)
    {
        // Think for 1 second, or 1/3 the remaining time if there's less than 3 seconds left
        var maxTimeMillis = timer.MillisecondsRemaining < 3000 ? timer.MillisecondsRemaining / 3 : 1000;

        searchAborted = false;
        var cancellationTimer = new System.Timers.Timer(maxTimeMillis);
        cancellationTimer.Elapsed += (s, e) =>
        {
            searchAborted = true;
            cancellationTimer.Stop();
        };
        cancellationTimer.Start();
        
        var (score, move) = IterativeDeepeningSearch(board, 10);
        Console.WriteLine($"Current eval: {Evaluate(board, board.GetLegalMoves())}, Best move score: {score}, Result: {move}, ttSize: {_transpositionTable.Count}, fen: {board.GetFenString()}");
        return move;
    }

    private (int, Move) IterativeDeepeningSearch(Board board, int maxDepth)
    {
        (int, Move) searchResult = (0, new Move());
        var searchDepth = 1;
        do
        {
            var result = Search(board, negativeInfinity, positiveInfinity, searchDepth);
            if (!searchAborted)
            {
                searchResult = result;
            }
        } while (searchDepth++ < maxDepth && !searchAborted);

        return searchResult;
    }
    
    private (int, Move) Search(Board board, int alpha, int beta, int depthLeft, int? initialDepth = null)
    {
        if (searchAborted)
        {
            return (0, new Move());
        }
        
        initialDepth ??= depthLeft;
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

    public static ulong GetPassedPawnBitboard(int rank, int file, bool isWhite)
    {
        return 
            (isWhite // Forward mask
                ? ulong.MaxValue << 8 * (rank + 1) // White, going up the board
                : ulong.MaxValue >> 8 * (8 - rank)) // Black, going down the board
            &
                (0x0101010101010101u << file // File mask
               | 0x0101010101010101u << Math.Max(0, file - 1) // Left file mask
               | 0x0101010101010101u << Math.Min(7, file + 1)); // Right file mask
    }
    
    private double GetPiecePositionalMultiplier(PieceType pieceType, Board board, bool isWhite, int rank, int file, double endgameModifier)
    {
        switch (pieceType)
        {
            case PieceType.Pawn:
                return 1 + (
                   isWhite 
                        // Distance from the 7th rank, with bonus for the distance from horizontal center
                        ? rank / 7d
                        : (7 - rank) / 7d
                ) * (1 + endgameModifier);
            case PieceType.Knight:
            case PieceType.Queen:
                // Encourage pieces to move towards the center of the board towards the beginning of the game
                // Multiply by (1.5 - endgameModifier) to reduce the effect of this as the game progresses
                return 1 + ((3.5 - Math.Min(Math.Abs(rank - 3.5), Math.Abs(file - 3.5))) / 3.5d) * (1.5 - endgameModifier);
            case PieceType.King:
                double extraWeighting = 0;
                var enemyKing = board.GetKingSquare(isWhite);

                // Encourage our king to move towards the enemy king, to "box it in"
                var distanceFromEnemyKing = Math.Abs(enemyKing.Rank - rank) + Math.Abs(enemyKing.File - file);
                extraWeighting += (14 - distanceFromEnemyKing) / 14d;

                // Encourage enemy king to move towards edge/corner
                var distanceFromCenter = Math.Abs(enemyKing.Rank - 3.5) + Math.Abs(enemyKing.File - 3.5);
                extraWeighting += (3.5 - distanceFromCenter) / 3.5d;

                return 1 + (extraWeighting * endgameModifier * endgameModifier);
        }

        return 1;
    }

    private readonly Dictionary<ulong, int> _transpositionTable = new();

    private Move[] GetOrderedLegalMoves(Board board, bool capturesOnly = false)
    {
        var legalMoves = board.GetLegalMoves(capturesOnly);
        return legalMoves.OrderByDescending((move) =>
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
                score += values[(int)move.CapturePieceType];
            }
            
            // Punish moves that cause the king to lose castling rights (we currently have castling rights, the move isn't a castling move, and the move is a king move)
            if ((board.HasKingsideCastleRight(board.IsWhiteToMove) ||
                 board.HasQueensideCastleRight(board.IsWhiteToMove)) 
                && !move.IsCastles
                && move.MovePieceType == PieceType.King)
            {
                    score -= 300;
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

            _transpositionTable.TryAdd(ttKey, eval);
            return eval;
        }
        
        if (_transpositionTable.ContainsKey(ttKey))
        {
            return _transpositionTable[ttKey];
        }

        var pieceLists = board.GetAllPieceLists();
        var piecesRemaining = CountBits(board.AllPiecesBitboard);
        
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
        var endgameModifier = Math.Max(Math.Min(-piecesRemaining/20d + 32/20d, 0), 1);
        
        foreach (var pieceList in pieceLists)
        {
            var pieceType = pieceList.TypeOfPieceInList;
            if (pieceType == PieceType.None) continue;
            foreach (var piece in pieceList)
            {
                var positionalMultiplier = GetPiecePositionalMultiplier(pieceType, board, piece.IsWhite,
                    piece.Square.Rank, piece.Square.File, endgameModifier);
                eval += (int)(values[(int)piece.PieceType] * positionalMultiplier) * (piece.IsWhite ? 1 : -1);
            }
        }

        _transpositionTable.Add(ttKey, eval);
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