using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static HoneyBadger.Globals;

namespace HoneyBadger
{
    public class EPDRunner
    {

        public void ExecuteEPDTests(string inputFile, string outputFile, int epdDepth)
        {
            string[] lines = File.ReadAllLines(inputFile);

            using (StreamWriter writer = new StreamWriter(outputFile))
            {
                Stopwatch totalTimer = Stopwatch.StartNew();

                foreach (string line in lines)
                {
                    Stopwatch positionTimer = Stopwatch.StartNew();

                    // Strip trailing directives (bm/am/id) to get pure FEN
                    string fenPart = line.Split("bm")[0].Split("am")[0].Trim();

                    Board board = new Board();
                    board.InitialiseFromFEN(fenPart);

                    Search search = new Search();
                    int bestScore;
                    Move bestMove = search.Iterate(board, epdDepth, 1000000, out bestScore);

                    positionTimer.Stop();

                    // Convert move to algebraic string for comparison
                    string moveString = $"{SquareToString(bestMove.FromSquare)}{SquareToString(bestMove.ToSquare)}";

                    // Write result line: include original line, engine move, score, and time
                    writer.WriteLine($"{line} | EngineMove={moveString} | Score={bestScore} | Time={positionTimer.ElapsedMilliseconds}ms");
                    writer.WriteLine("Nodes: " + search.NodesSearched.ToString());
                    writer.WriteLine(); // blank line for spacing
                }

                totalTimer.Stop();

                // Write summary line for total time
                writer.WriteLine($"TotalTime={totalTimer.ElapsedMilliseconds}ms for {lines.Length} positions");
            }
        }







    }
}
