using static HoneyBadger.Globals;
using static HoneyBadger.Eval;
using static HoneyBadger.MoveOrdering;
namespace HoneyBadger
{
    public class Search
    {

        private TranspositionTable? _transTable;
        private Move?[][] _killerMoves = new Move?[40][];
        private int[][] _history = new int[13][];


        private long _nodesSearched;

        public long NodesSearched
        {
            get
            {
                return _nodesSearched;
            }
        }

        public Move Iterate(Board board, int maxDepth, int millisecondsForMove, out int bestScore)
        {
            Move bestMove = default(Move);
            bestScore = 0;
            _nodesSearched = 0;

            _transTable = new(100);

            for (int nn = 0; nn < 40; nn++)
            {
                _killerMoves[nn] = new Move?[2];
            }

            for (int nn = 0; nn < 13; nn++)
            {
                _history[nn] = new int[128];
            }

            DateTime startTime = DateTime.UtcNow;

            // Initial aspiration window size (non‑aggressive)
            int aspirationDelta = 50;

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                // Check time budget
                TimeSpan elapsed = DateTime.UtcNow - startTime;
                if (elapsed.TotalMilliseconds >= millisecondsForMove)
                {
                    break;
                }



                int alpha = bestScore - aspirationDelta;
                int beta = bestScore + aspirationDelta;

                Move moveAtThisDepth;
                int scoreAtThisDepth;

                // Aspiration loop: widen window if fail‑low/high
                while (true)
                {
                    scoreAtThisDepth = AlphaBeta(out moveAtThisDepth, board, depth, depth, alpha, beta, true);

                    if (scoreAtThisDepth <= alpha)
                    {
                        // Fail‑low: widen downward
                        alpha -= aspirationDelta;
                        aspirationDelta = aspirationDelta * 2; // widen progressively
                    }
                    else if (scoreAtThisDepth >= beta)
                    {
                        // Fail‑high: widen upward
                        beta += aspirationDelta;
                        aspirationDelta = aspirationDelta * 2;
                    }
                    else
                    {
                        // Score inside window: success
                        break;
                    }

                    if (scoreAtThisDepth <= -CHECKMATE || scoreAtThisDepth >= CHECKMATE)
                    {
                        break;
                    }

                    // Optional: break if time exceeded mid‑search
                    elapsed = DateTime.UtcNow - startTime;
                    if (elapsed.TotalMilliseconds >= millisecondsForMove)
                    {
                        break;
                    }
                }

                // Update best move/score
                bestMove = moveAtThisDepth;
                bestScore = scoreAtThisDepth;

                // Reset aspirationDelta for next iteration
                aspirationDelta = 50;

                // Optional: break if time exceeded mid‑search
                elapsed = DateTime.UtcNow - startTime;
                if (elapsed.TotalMilliseconds >= millisecondsForMove)
                {
                    break;
                }
            }

            return bestMove;
        }



        private int AlphaBeta(out Move bestMove, Board board, int depth, int fullDepth, int alpha, 
            int beta, bool isRoot, int ply = 0, Move? previousMove = null)
        {
            bestMove = default(Move);
            _nodesSearched++;

            // --- Base case: leaf node ---
            if (depth == 0)
            {
                return Quiescence(board, alpha, beta, ply);
            }

            // Futility pruning: only if fullDepth >= 6 and local depth == 1
            if (fullDepth >= 6 && depth == 1)
            {
                int eval = ScorePosition(board);
                if (eval + (BISHOP_VALUE + 50) <= alpha)
                {
                    return alpha;
                }
            }

            // Extended futility pruning: depth == 2 uses rook margin
            if (fullDepth >= 8 && depth == 2)
            {
                int eval = ScorePosition(board);
                if (eval + (ROOK_VALUE + 50) <= alpha)
                {
                    return alpha;
                }
            }

            // Extended futility pruning: depth == 3 uses queen margin
            if (fullDepth >= 8 && depth == 3)
            {
                int eval = ScorePosition(board);
                if (eval + (QUEEN_VALUE + 50) <= alpha)
                {
                    return alpha;
                }
            }

            // Razoring: only if fullDepth >= 8 and local depth == 1
            if (fullDepth >= 8 && depth == 1)
            {
                int eval = ScorePosition(board);
                if (eval + (PAWN_VALUE + 50) < alpha)
                {
                    return Quiescence(board, alpha, beta, ply);
                }
            }

            int alphaOrig = alpha;

            // --- Probe transposition table ---
            bool ttHit = _transTable!.Probe(board.CurrentZobrist, out TTEntry ttEntry);
            if (ttHit)
            {
                if (ttEntry.Depth >= depth)
                {
                    switch (ttEntry.Type)
                    {
                        case TTNodeType.Exact:
                            bestMove = ttEntry.BestMove;
                            return ttEntry.Score;

                        case TTNodeType.LowerBound:
                            if (ttEntry.Score >= beta)
                            {
                                return ttEntry.Score;
                            }
                            break;

                        case TTNodeType.UpperBound:
                            if (ttEntry.Score <= alpha)
                            {
                                return ttEntry.Score;
                            }
                            break;
                    }
                }

                // If root, use stored best move for ordering
                if (isRoot)
                {
                    board.GenerateAllMoves(ply);
                    SortMoves(board.GeneratedMoves(ply), _history, board.GeneratedMoveCount(ply), ttEntry.BestMove,
                        _killerMoves[ply][0], _killerMoves[ply][1]);
                }
                else
                {
                    board.GenerateAllMoves(ply);
                    SortMoves(board.GeneratedMoves(ply), _history, board.GeneratedMoveCount(ply), null,
                        _killerMoves[ply][0], _killerMoves[ply][1]);
                }
            }
            else
            {
                board.GenerateAllMoves(ply);
                SortMoves(board.GeneratedMoves(ply), _history, board.GeneratedMoveCount(ply), null,
                    _killerMoves[ply][0], _killerMoves[ply][1]);
            }

            int bestScore = int.MinValue + 1;

            for (int nn = 0; nn < board.GeneratedMoveCount(ply); nn++)
            {
                Move move = board.GeneratedMoves(ply)[nn];

                if (!board.MoveIsLegal(move))
                {
                    continue;
                }

                int newDepth = depth - 1;
                if (depth >= 3 && nn >= 3 && move.CapturedPiece == Piece.None)
                {
                    // Reduce depth by 1 (or more depending on depth/move index)
                    newDepth = depth - 2;
                }

                board.MakeMove(move);
                int score = -AlphaBeta(out _, board, newDepth, fullDepth, -beta, -alpha, false, ply + 1, move);
                board.UnmakeMove(move);

                if (score > alpha && newDepth == depth - 2)
                {
                    board.MakeMove(move);
                    score = -AlphaBeta(out _, board, depth - 1, fullDepth, -beta, -alpha, false, ply + 1, move);
                    board.UnmakeMove(move);
                }



                if (score > bestScore)
                {
                    bestScore = score;

                    if (isRoot)
                    {
                        bestMove = move;
                    }
                }

                if (bestScore > alpha)
                {
                    alpha = bestScore;
                }

                if (alpha >= beta)
                {
                    if (move.CapturedPiece == Piece.None)
                    {
                        if (_killerMoves[ply][0] == null || !move.Equals(_killerMoves[ply][0]!.Value))
                        {
                            _killerMoves[ply][1] = _killerMoves[ply][0];
                            _killerMoves[ply][0] = move;
                        }
                    }
                    _history[(int)move.MovingPiece][move.ToSquare] += depth * depth;
                    break; // Beta cutoff
                }
            }

            // --- Store result in transposition table ---
            TTNodeType type;

            if (bestScore <= alphaOrig)
            {
                type = TTNodeType.UpperBound;
            }
            else if (bestScore >= beta)
            {
                type = TTNodeType.LowerBound;
            }
            else
            {
                type = TTNodeType.Exact;
            }

            _transTable.Store(board.CurrentZobrist, bestMove, (byte)depth, bestScore, type, (byte)ply);

            return bestScore;
        }


        // Quiescence search using Board's per-ply move buffers and legality checks.
        // NOTE: Replace `board.IsSideToMoveInCheck()` with your actual check-detection call.
        private int Quiescence(Board board, int alpha, int beta, int ply, int checkExtensions = 0)
        {
            _nodesSearched++;
            bool inCheck = board.IsInCheck(board.SideToMove); 

            if (inCheck)
            {
                if (checkExtensions >= 3)
                {
                    // Bail out: just return static eval to avoid infinite recursion
                    return ScorePosition(board);
                }

                // Must search ALL legal evasions when in check
                board.GenerateAllMoves(ply);
                int moveCount = board.GeneratedMoveCount(ply);

                bool anyLegal = false;

                for (int nn = 0; nn < moveCount; nn++)
                {
                    Move m = board.GeneratedMoves(ply)[nn];

                    if (!board.MoveIsLegal(m))
                    {
                        continue;
                    }

                    anyLegal = true;

                    board.MakeMove(m);
                    int score = -Quiescence(board, -beta, -alpha, ply + 1, checkExtensions + 1);
                    board.UnmakeMove(m);

                    if (score >= beta)
                    {
                        return beta; // fail-hard cutoff
                    }
                    if (score > alpha)
                    {
                        alpha = score;
                    }
                }

                // If in check and no legal moves, it's checkmate
                if (!anyLegal)
                {
                    return -CHECKMATE + ply; // ply as a tie-break (longer mates are worse)
                }

                return alpha;
            }

            // Stand-pat in quiet positions
            int standPat = ScorePosition(board);
            if (standPat >= beta)
            {
                return beta;
            }
            if (standPat > alpha)
            {
                alpha = standPat;
            }

            // Only explore captures to reach a quiet position
            board.GenerateAllCaptures(ply);
            int capCount = board.GeneratedMoveCount(ply);

            for (int nn = 0; nn < capCount; nn++)
            {
                Move m = board.GeneratedMoves(ply)[nn];

                if (!board.MoveIsLegal(m))
                {
                    continue;
                }

                board.MakeMove(m);
                int score = -Quiescence(board, -beta, -alpha, ply + 1, checkExtensions);
                board.UnmakeMove(m);

                if (score >= beta)
                {
                    return beta;
                }
                if (score > alpha)
                {
                    alpha = score;
                }
            }

            return alpha;
        }

    }

}
