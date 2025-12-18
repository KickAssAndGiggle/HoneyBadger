using HoneyBadger;
using static HoneyBadger.Globals;
using static System.Formats.Asn1.AsnWriter;

namespace HoneyBadger
{
    public static class MoveOrdering
    {
        // Helper: map Piece enum to material value
        private static int PieceScore(Piece piece)
        {
            switch (piece)
            {
                case Piece.WhitePawn:
                case Piece.BlackPawn:
                    return PAWN_VALUE;
                case Piece.WhiteKnight:
                case Piece.BlackKnight:
                    return KNIGHT_VALUE;
                case Piece.WhiteBishop:
                case Piece.BlackBishop:
                    return BISHOP_VALUE;
                case Piece.WhiteRook:
                case Piece.BlackRook:
                    return ROOK_VALUE;
                case Piece.WhiteQueen:
                case Piece.BlackQueen:
                    return QUEEN_VALUE;
                case Piece.WhiteKing:
                case Piece.BlackKing:
                    return KING_VALUE;
                default:
                    return 0;
            }
        }

        public static void SortMoves(Move[] moves, int[][] history, int moveCount, Move? pvMove = null, 
            Move? killerOne = null, Move? killerTwo = null)
        {
            for (int nn = 0; nn < moves.Length; nn++)
            {
                if (nn >= moveCount)
                {
                    moves[nn].SortScore = 0; // unused slots
                    continue;
                }

                Move m = moves[nn];

                if (pvMove != null && m.Equals(pvMove.Value))
                {
                    // Force PV move to highest priority
                    m.SortScore = 255;
                }
                else if (m.CapturedPiece != Piece.None)
                {
                    // MVV/LVA scoring: victim value minus attacker value
                    int victim = PieceScore(m.CapturedPiece);
                    int attacker = PieceScore(m.MovingPiece);

                    int score = victim - attacker / 10; // crude tie‑breaker
                    if (score > 254)
                    {
                        score = 254; // leave 255 for PV move and 254 for recapture last move
                    }
                    else if (score < 100)
                    {
                        score = 100; // captures never below 50
                    }
                    m.SortScore = (byte)score;
                }
                else
                {
                    if (killerOne != null && m.ToSquare == killerOne.Value.ToSquare && m.FromSquare == killerOne.Value.FromSquare)
                    {
                        m.SortScore = 99;
                    }
                    else if (killerTwo != null && m.ToSquare == killerTwo.Value.ToSquare && m.FromSquare == killerTwo.Value.FromSquare)
                    {
                        m.SortScore = 98;
                    }
                    else
                    {
                        // Quiet moves use history
                        m.SortScore = (byte)Math.Min(0 + history[(int)m.MovingPiece][m.ToSquare] >> 10, 97);
                    }
                }

                moves[nn] = m; // reassign back into array
            }

            // Now sort in place using CompareTo
            Array.Sort(moves, 0, moveCount);
        }


    }
}

