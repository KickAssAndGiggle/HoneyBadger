using static HoneyBadger.Globals;
namespace HoneyBadger
{
    public static class Eval
    {


        public static int ScorePosition(Board board)
        {
            int score = 0;

            Piece[] squares = board.Squares;

            for (int nn = 0; nn < squares.Length; nn++)
            {
                // Skip off-board squares in 0x88
                if ((nn & 0x88) != 0)
                {
                    continue;
                }

                Piece piece = squares[nn];

                if (piece != Piece.None)
                {
                    ScorePieceOnSquare(nn, piece, ref score);
                }
            }

            return board.SideToMove == Color.White ? score : -score;
        }


        private static void ScorePieceOnSquare(int sq, Piece piece, ref int score)
        {
            int rank = sq >> 4;
            int file = sq & 0xF;

            switch (piece)
            {
                case Piece.WhitePawn:
                    score += Globals.PAWN_VALUE;
                    if ((file == 2 || file == 3 || file == 4) && rank == 3) // c4, d4, e4
                    {
                        score += 20;
                    }
                    break;

                case Piece.BlackPawn:
                    score -= Globals.PAWN_VALUE;
                    if ((file == 2 || file == 3 || file == 4) && rank == 4) // c5, d5, e5
                    {
                        score -= 20;
                    }
                    break;

                case Piece.WhiteKnight:
                    score += Globals.KNIGHT_VALUE;
                    if (rank == 0) // back rank
                    {
                        score -= 15;
                    }
                    break;

                case Piece.BlackKnight:
                    score -= Globals.KNIGHT_VALUE;
                    if (rank == 7) // back rank
                    {
                        score += 15;
                    }
                    break;

                case Piece.WhiteBishop:
                    score += Globals.BISHOP_VALUE;
                    if (rank == 0)
                    {
                        score -= 15;
                    }
                    break;

                case Piece.BlackBishop:
                    score -= Globals.BISHOP_VALUE;
                    if (rank == 7)
                    {
                        score += 15;
                    }
                    break;

                case Piece.WhiteRook:
                    score += Globals.ROOK_VALUE;
                    break;

                case Piece.BlackRook:
                    score -= Globals.ROOK_VALUE;
                    break;

                case Piece.WhiteQueen:
                    score += Globals.QUEEN_VALUE;
                    if (rank == 0)
                    {
                        score -= 15;
                    }
                    break;

                case Piece.BlackQueen:
                    score -= Globals.QUEEN_VALUE;
                    if (rank == 7)
                    {
                        score += 15;
                    }
                    break;

                case Piece.WhiteKing:
                    score += Globals.KING_VALUE;
                    break;

                case Piece.BlackKing:
                    score -= Globals.KING_VALUE;
                    break;
            }
        }



    }
}
