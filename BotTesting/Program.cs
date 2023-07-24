// See https://aka.ms/new-console-template for more information

using ChessChallenge.API;

var board = new ChessChallenge.Chess.Board();
var bot = new MyBot();
while (true)
{
    var timer = new ChessChallenge.API.Timer(10000);

    Console.WriteLine("Enter FEN:");
    var fen = Console.ReadLine();
    if (fen != null)
    {
        board.LoadPosition(fen);
    }
    else
    {
        board.LoadStartPosition();
    }
    
    var move = bot.Think(new Board(board), timer);
    Console.WriteLine(move);
    Console.WriteLine("\n\n");
}