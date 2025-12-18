using static HoneyBadger.Globals;
namespace HoneyBadger
{
    internal class Program
    {

        static void Main(string[] args)
        {


            string a = "C:\\EPDs\\First10WAC.epd";
            Console.WriteLine($"EPD mode with file: {a}");
            EPDRunner runner2 = new();
            runner2.ExecuteEPDTests(a, "C:\\psoutput\\test.txt", 8);
            Environment.Exit(0);

            // Default mode is UCI if none specified
            string mode = "uci";
            string positionFen = "";
            string epdFilePath = "";

            // Parse arguments of form key=value
            foreach (string arg in args)
            {
                string[] parts = arg.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                string key = parts[0].Trim().ToLower();
                string value = parts[1].Trim();

                switch (key)
                {
                    case "mode":
                        mode = value.ToLowerInvariant();
                        break;

                    case "position":
                        positionFen = value;
                        break;

                    case "file":
                        epdFilePath = value;
                        break;
                }
            }

            // Dispatch based on mode
            switch (mode.ToLowerInvariant())
            {
                case "perft":
                    // For now just store the FEN in a variable
                    string perftFen = positionFen;
                    Console.WriteLine($"Perft mode with position: {perftFen}");
                    break;

                case "epd":
                    // For now just store the file path in a variable
                    string epdPath = epdFilePath;
                    Console.WriteLine($"EPD mode with file: {epdPath}");
                    EPDRunner runner = new();
                    runner.ExecuteEPDTests(epdPath, "C:\\psoutput\\test.txt", 6);
                    break;

                default:
                    // Default to UCI mode
                    Console.WriteLine("UCI mode");
                    break;
            }
        }



        //static void Main(string[] args)
        //{

        //    Board perftBoard = new();
        //    //perftBoard.InitialiseFromFEN("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1 "); //start pos
        //    //perftBoard.InitialiseFromFEN("rnbqkbnr/pppppppp/8/8/8/2N5/PPPPPPPP/R1BQKBNR b KQkq - 1 1"); // After 1. Nc3
        //    //perftBoard.InitialiseFromFEN("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2"); // 1. e4 e5
        //    perftBoard.InitialiseFromFEN("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPPQPPP/RNB1KBNR b KQkq - 1 2"); // 1.e4 e5 2. Qe2

        //    Search srch = new Search();
        //    Move m = srch.Iterate(perftBoard, 6, 60000, out int score);

        //    Console.WriteLine(SquareToString(m.FromSquare) + "-" + SquareToString(m.ToSquare) + "   " + (score / 100).ToString());

        //    //long runningTotal = 0;
        //    //Perft(perftBoard, 1, 7, ref runningTotal);

        //    //Console.WriteLine(runningTotal.ToString());

    }

    //private static void Perft(Board board, int currentDepth, int fullDepth, ref long runningTotal)
    //    {

    //        //board.GenerateAllMoves();
    //        //if (currentDepth == fullDepth)
    //        //{
    //        //    for (int nn = 0; nn < board.GeneratedMoveCount; nn++)
    //        //    {
    //        //        if (board.MoveIsLegal(board.GeneratedMoves[nn]))
    //        //        {
    //        //            runningTotal++;
    //        //        }
    //        //    }
    //        //}
    //        //else
    //        //{
    //        //    for (int nn = 0; nn < board.GeneratedMoveCount; nn++)
    //        //    {
    //        //        if (board.MoveIsLegal(board.GeneratedMoves[nn]))
    //        //        {
    //        //            Move current = board.GeneratedMoves[nn];
    //        //            Move abc = new();
    //        //            abc.FromSquare = current.FromSquare;
    //        //            abc.ToSquare = current.ToSquare;
    //        //            board.MakeMove(current);
    //        //            Perft(board, currentDepth + 1, fullDepth, ref runningTotal);
    //        //            board.UnmakeMove(current);
    //        //            if (current.FromSquare != abc.FromSquare || current.ToSquare != abc.ToSquare)
    //        //            {
    //        //                int xx = 1;
    //        //            }
    //        //            board.GenerateAllMoves();
    //        //        }
    //        //    }
    //        //}
    //    }
    //}
}
