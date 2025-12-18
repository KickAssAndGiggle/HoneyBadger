using System;
using System.Runtime.CompilerServices;
using static HoneyBadger.Globals;

namespace HoneyBadger
{
    public sealed class Board
    {
        // 0x88 mailbox
        private Piece[] _squares = new Piece[Globals.BOARD_SIZE];

        // Side to move
        private Color _sideToMove;

        // Castling rights bitmask: WK=1, WQ=2, BK=4, BQ=8
        private int _castlingRights;

        // En passant square (-1 if none)
        private int _enPassantSquare;

        // Halfmove clock for 50-move rule
        private int _halfmoveClock;

        // Fullmove number
        private int _fullmoveNumber;

        // History stack (for unmake)
        private int[] _castlingHistory = new int[60];
        private int[] _enPassantHistory = new int[60];
        private int[] _halfmoveHistory = new int[60];
        private Piece[] _capturedHistory = new Piece[60];
        private ulong[] _zobristHistory = new ulong[60];
        private ulong[] _pawnZobristHistory = new ulong[60];
        private int _historyPly = 0;

        private int _whiteKingSquare = -1;
        private int _blackKingSquare = -1;

        private Move[][] _generatedMoves = new Move[40][]; // 40 plies, will be 256 per ply generous upper bound per position
        private int[] _generatedMoveCount = new int[40]; // Generated move counts, for 40 plies
        private int _generatorPly = -1;

        // Zobrist fields
        private ulong _currentZobrist;
        private ulong _pawnOnlyZobrist;

        // Random tables
        private readonly ulong[,] _zobristPieceSquares = new ulong[13, 128]; // Piece × square (0x88)
        private readonly ulong[] _zobristCastling = new ulong[16];           // 4 bits of castling rights
        private readonly ulong[] _zobristEnPassant = new ulong[8];           // file a–h
        private readonly ulong _zobristBlackToMove;



        public Board()
        {
            for (int nn = 0; nn < 40; nn++)
            {
                _generatedMoves[nn] = new Move[256];
                _generatedMoveCount[nn] = 0;
            }

            Random rnd = new Random(1762353731); // fixed seed for reproducibility
            byte[] buffer = new byte[8];

            // Piece-square randoms
            for (int nn = 0; nn < 13; nn++) // Piece.None through BlackKing
            {
                for (int sq = 0; sq < 128; sq++)
                {
                    if ((sq & 0x88) == 0) // valid 0x88 square
                    {
                        rnd.NextBytes(buffer);
                        _zobristPieceSquares[nn, sq] = BitConverter.ToUInt64(buffer, 0);
                    }
                }
            }

            // Castling rights (16 possible bitmasks)
            for (int nn = 0; nn < 16; nn++)
            {
                rnd.NextBytes(buffer);
                _zobristCastling[nn] = BitConverter.ToUInt64(buffer, 0);
            }

            // En passant files
            for (int nn = 0; nn < 8; nn++)
            {
                rnd.NextBytes(buffer);
                _zobristEnPassant[nn] = BitConverter.ToUInt64(buffer, 0);
            }

            // Side to move
            rnd.NextBytes(buffer);
            _zobristBlackToMove = BitConverter.ToUInt64(buffer, 0);

        }

        public Move[] GeneratedMoves(int ply)
        {
            return _generatedMoves[ply];
        }

        public int GeneratedMoveCount(int ply)
        {
            return _generatedMoveCount[ply];
        }

        public Piece[] Squares
        {
            get { return _squares; }
        }

        public Color SideToMove
        {
            get { return _sideToMove; }
        }

        public ulong PawnOnlyZobrist
        {
            get { return _pawnOnlyZobrist; }
        }

        public ulong CurrentZobrist
        {
            get { return _currentZobrist; }
        }


        public void InitialiseFromFEN(string fen)
        {
            for (int nn = 0; nn < Globals.BOARD_SIZE; nn++)
            {
                _squares[nn] = Piece.None;
            }

            _castlingRights = 0;
            _enPassantSquare = -1;
            _halfmoveClock = 0;
            _fullmoveNumber = 1;

            string[] parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string[] ranks = parts[0].Split('/');
            int rankIndex = 7;

            foreach (string rank in ranks)
            {
                int fileIndex = 0;
                foreach (char symbol in rank)
                {
                    if (char.IsDigit(symbol))
                    {
                        int emptyCount = symbol - '0';
                        fileIndex += emptyCount;
                    }
                    else
                    {
                        int square = (rankIndex << 4) | fileIndex;
                        Piece piece = CharToPiece(symbol);
                        _squares[square] = piece;

                        if (piece != Piece.None)
                        {
                            if (piece <= Piece.WhiteKing)
                            {
                                if (piece == Piece.WhiteKing)
                                {
                                    _whiteKingSquare = square;
                                }
                            }
                            else
                            {
                                if (piece == Piece.BlackKing)
                                {
                                    _blackKingSquare = square;
                                }
                            }
                        }

                        fileIndex++;
                    }
                }
                rankIndex--;
            }

            _sideToMove = parts[1] == "w" ? Color.White : Color.Black;

            string castling = parts[2];
            if (castling.Contains("K")) { _castlingRights |= 1; }
            if (castling.Contains("Q")) { _castlingRights |= 2; }
            if (castling.Contains("k")) { _castlingRights |= 4; }
            if (castling.Contains("q")) { _castlingRights |= 8; }

            string ep = parts[3];
            if (ep != "-")
            {
                int file = ep[0] - 'a';
                int rank = ep[1] - '1';
                _enPassantSquare = (rank << 4) | file;
            }
            else
            {
                _enPassantSquare = -1;
            }

            if (parts.Length > 4)
            {
                _halfmoveClock = int.Parse(parts[4]);
            }

            if (parts.Length > 5)
            {
                _fullmoveNumber = int.Parse(parts[5]);
            }

            SetInitialZobrist();
        }


        private void SetInitialZobrist()
        {
            _currentZobrist = 0;
            _pawnOnlyZobrist = 0;

            for (int sq = 0; sq < 128; sq++)
            {
                if ((sq & 0x88) == 0)
                {
                    Piece p = Squares[sq]; // your board array
                    if (p != Piece.None)
                    {
                        _currentZobrist ^= _zobristPieceSquares[(int)p, sq];
                        if (p == Piece.WhitePawn || p == Piece.BlackPawn)
                        {
                            _pawnOnlyZobrist ^= _zobristPieceSquares[(int)p, sq];
                        }
                    }
                }
            }

            // En passant
            if (_enPassantSquare != -1)
            {
                int file = _enPassantSquare & 7;
                _currentZobrist ^= _zobristEnPassant[file];
            }

            // Side to move
            if (SideToMove == Color.Black)
            {
                _currentZobrist ^= _zobristBlackToMove;
            }

            // Castling rights
            _currentZobrist ^= _zobristCastling[_castlingRights];
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TogglePiece(Piece piece, int sq)
        {
            _currentZobrist ^= _zobristPieceSquares[(int)piece, sq];
            if (piece == Piece.WhitePawn || piece == Piece.BlackPawn)
            {
                _pawnOnlyZobrist ^= _zobristPieceSquares[(int)piece, sq];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ToggleSideToMove()
        {
            _currentZobrist ^= _zobristBlackToMove;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ToggleCastling(int mask)
        {
            _currentZobrist ^= _zobristCastling[mask];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ToggleEnPassant(int file)
        {
            _currentZobrist ^= _zobristEnPassant[file];
        }


        public bool MoveIsLegal(Move move)
        {

            // Determine the moving side before MakeMove flips it
            Piece movingPiece = _squares[move.FromSquare];
            Color movingColor = (movingPiece <= Piece.WhiteKing) ? Color.White : Color.Black;

            // --- Special handling for castling ---
            if (move.Flags == MOVE_FLAG_CASTLE_K || move.Flags == MOVE_FLAG_CASTLE_Q)
            {
                Color opponentColor = (movingColor == Color.White) ? Color.Black : Color.White;
                if (movingColor == Color.White)
                {
                    if (move.Flags == MOVE_FLAG_CASTLE_K)
                    {
                        if (SquareIsAttacked(0x04, Color.Black))
                        {
                            return false;
                        }
                        if (SquareIsAttacked(0x05, Color.Black))
                        {
                            return false;
                        }
                        if (SquareIsAttacked(0x06, Color.Black))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (SquareIsAttacked(0x04, Color.Black))
                        {
                            return false;
                        }
                        if (SquareIsAttacked(0x03, Color.Black))
                        {
                            return false;
                        }
                        if (SquareIsAttacked(0x02, Color.Black))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    if (move.Flags == MOVE_FLAG_CASTLE_K)
                    {
                        if (SquareIsAttacked(0x74, Color.White))
                        {
                            return false;
                        }
                        if (SquareIsAttacked(0x75, Color.White))
                        {
                            return false;
                        }
                        if (SquareIsAttacked(0x76, Color.White))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (SquareIsAttacked(0x74, Color.White))
                        {
                            return false;
                        }
                        if (SquareIsAttacked(0x73, Color.White))
                        {
                            return false;
                        }
                        if (SquareIsAttacked(0x72, Color.White))
                        {
                            return false;
                        }
                    }
                }
            }
            
            // Make the move
            MakeMove(move);

            // Check if our own king is attacked
            bool inCheck = IsInCheck(movingColor);

            // Undo the move
            UnmakeMove(move);

            return !inCheck;

        }



        //public void MakeMove(Move move)
        //{
        //    // --- Save history ---
        //    _castlingHistory[_historyPly] = _castlingRights;
        //    _enPassantHistory[_historyPly] = _enPassantSquare;
        //    _halfmoveHistory[_historyPly] = _halfmoveClock;
        //    _capturedHistory[_historyPly] = move.CapturedPiece;
        //    _zobristHistory[_historyPly] = _currentZobrist; // placeholder, Zobrist later
        //    _pawnZobristHistory[_historyPly] = _pawnOnlyZobrist;

        //    _historyPly++;

        //    Piece movingPiece = _squares[move.FromSquare];
        //    Piece capturedPiece = move.CapturedPiece;

        //    // --- Clear en passant square ---
        //    _enPassantSquare = -1;

        //    // --- Handle captures ---
        //    if (capturedPiece != Piece.None)
        //    {
        //        _squares[move.ToSquare] = Piece.None; // remove captured
        //        _halfmoveClock = 0;
        //    }

        //    // --- Move piece ---
        //    _squares[move.FromSquare] = Piece.None;
        //    _squares[move.ToSquare] = movingPiece;

        //    // --- Promotions ---
        //    if (move.PromotionPiece != Piece.None)
        //    {
        //        _squares[move.ToSquare] = move.PromotionPiece;
        //        _halfmoveClock = 0;
        //    }

        //    if (movingPiece == Piece.WhiteKing)
        //    {
        //        _whiteKingSquare = move.ToSquare;
        //        _castlingRights &= ~(1 | 2); // revoke both White rights
        //    }
        //    else if (movingPiece == Piece.BlackKing)
        //    {
        //        _blackKingSquare = move.ToSquare;
        //        _castlingRights &= ~(4 | 8); // revoke both Black rights
        //    }

        //    if (move.FromSquare == 0x00 || move.ToSquare == 0x00)
        //    {
        //        _castlingRights &= ~2; // Remove white Q Side castling rights
        //    }
        //    else if (move.FromSquare == 0x07 || move.ToSquare == 0x07)
        //    {
        //        _castlingRights &= ~1; // Remove white K Side castling rights
        //    }
        //    else if (move.FromSquare == 0x70 || move.ToSquare == 0x70)
        //    {
        //        _castlingRights &= ~8; // Remove black Q Side castling rights
        //    }
        //    else if (move.FromSquare == 0x77 || move.ToSquare == 0x77)
        //    {
        //        _castlingRights &= ~4; // Remove black K Side castling rights
        //    }

        //    // --- Special flags ---
        //    if (move.Flags == MOVE_FLAG_DOUBLE_PAWN_PUSH)
        //    {
        //        if (move.Flags == MOVE_FLAG_DOUBLE_PAWN_PUSH)
        //        {
        //            if (movingPiece == Piece.WhitePawn)
        //            {
        //                _enPassantSquare = move.FromSquare + 16;
        //            }
        //            else
        //            {
        //                _enPassantSquare = move.FromSquare - 16;
        //            }
        //        }


        //    }
        //    if (move.Flags == MOVE_FLAG_EP_CAPTURE)
        //    {
        //        // En passant capture: remove pawn behind target square
        //        int epPawnSquare = (movingPiece <= Piece.WhiteKing)
        //            ? move.ToSquare - Globals.NORTH
        //            : move.ToSquare - Globals.SOUTH;
        //        _squares[epPawnSquare] = Piece.None;
        //    }
        //    if (move.Flags == MOVE_FLAG_CASTLE_K || move.Flags == MOVE_FLAG_CASTLE_Q)
        //    {
        //        // Castling: move rook
        //        if (move.ToSquare == 0x06) // white king-side
        //        {
        //            _squares[0x07] = Piece.None;
        //            _squares[0x05] = Piece.WhiteRook;
        //        }
        //        else if (move.ToSquare == 0x02) // white queen-side
        //        {
        //            _squares[0x00] = Piece.None;
        //            _squares[0x03] = Piece.WhiteRook;
        //        }
        //        else if (move.ToSquare == 0x76) // black king-side
        //        {
        //            _squares[0x77] = Piece.None;
        //            _squares[0x75] = Piece.BlackRook;
        //        }
        //        else if (move.ToSquare == 0x72) // black queen-side
        //        {
        //            _squares[0x70] = Piece.None;
        //            _squares[0x73] = Piece.BlackRook;
        //        }
        //    }

        //    // --- Update clocks ---
        //    if (movingPiece == Piece.WhitePawn || movingPiece == Piece.BlackPawn || capturedPiece != Piece.None)
        //    {
        //        _halfmoveClock = 0;
        //    }
        //    else
        //    {
        //        _halfmoveClock++;
        //    }

        //    if (_sideToMove == Color.Black)
        //    {
        //        _fullmoveNumber++;
        //    }

        //    // --- Switch side ---
        //    _sideToMove = (_sideToMove == Color.White) ? Color.Black : Color.White;
        //}

        public void MakeMove(Move move)
        {
            // --- Save history ---
            _castlingHistory[_historyPly] = _castlingRights;
            _enPassantHistory[_historyPly] = _enPassantSquare;
            _halfmoveHistory[_historyPly] = _halfmoveClock;
            _capturedHistory[_historyPly] = move.CapturedPiece;
            _zobristHistory[_historyPly] = _currentZobrist;
            _pawnZobristHistory[_historyPly] = _pawnOnlyZobrist;
            _historyPly++;

            Piece movingPiece = _squares[move.FromSquare];
            Piece capturedPiece = move.CapturedPiece;

            // --- Clear en passant square (toggle old if any) ---
            if (_enPassantSquare != -1)
            {
                ToggleEnPassant(_enPassantSquare & 7);
                _enPassantSquare = -1;
            }

            // --- Handle captures ---
            if (capturedPiece != Piece.None)
            {
                // Toggle captured piece out at destination square
                TogglePiece(capturedPiece, move.ToSquare);
                _squares[move.ToSquare] = Piece.None;
                _halfmoveClock = 0;
            }

            // --- Move piece (toggle out/in) ---
            TogglePiece(movingPiece, move.FromSquare);
            _squares[move.FromSquare] = Piece.None;

            TogglePiece(movingPiece, move.ToSquare);
            _squares[move.ToSquare] = movingPiece;

            // --- Promotions ---
            if (move.PromotionPiece != Piece.None)
            {
                // Replace pawn with promoted piece
                TogglePiece(movingPiece, move.ToSquare);
                TogglePiece(move.PromotionPiece, move.ToSquare);
                _squares[move.ToSquare] = move.PromotionPiece;
                _halfmoveClock = 0;
            }

            // --- King moves revoke castling rights (toggle old -> update -> toggle new) ---
            if (movingPiece == Piece.WhiteKing)
            {
                _whiteKingSquare = move.ToSquare;
                ToggleCastling(_castlingRights);
                _castlingRights &= ~(1 | 2);
                ToggleCastling(_castlingRights);
            }
            else if (movingPiece == Piece.BlackKing)
            {
                _blackKingSquare = move.ToSquare;
                ToggleCastling(_castlingRights);
                _castlingRights &= ~(4 | 8);
                ToggleCastling(_castlingRights);
            }

            // --- Rook moves revoke castling rights ---
            if (move.FromSquare == 0x00 || move.ToSquare == 0x00)
            {
                ToggleCastling(_castlingRights);
                _castlingRights &= ~2; // white queen-side
                ToggleCastling(_castlingRights);
            }
            else if (move.FromSquare == 0x07 || move.ToSquare == 0x07)
            {
                ToggleCastling(_castlingRights);
                _castlingRights &= ~1; // white king-side
                ToggleCastling(_castlingRights);
            }
            else if (move.FromSquare == 0x70 || move.ToSquare == 0x70)
            {
                ToggleCastling(_castlingRights);
                _castlingRights &= ~8; // black queen-side
                ToggleCastling(_castlingRights);
            }
            else if (move.FromSquare == 0x77 || move.ToSquare == 0x77)
            {
                ToggleCastling(_castlingRights);
                _castlingRights &= ~4; // black king-side
                ToggleCastling(_castlingRights);
            }

            // --- Special flags ---
            if (move.Flags == MOVE_FLAG_DOUBLE_PAWN_PUSH)
            {
                _enPassantSquare = (movingPiece == Piece.WhitePawn)
                    ? move.FromSquare + 16
                    : move.FromSquare - 16;

                ToggleEnPassant(_enPassantSquare & 7);
            }

            if (move.Flags == MOVE_FLAG_EP_CAPTURE)
            {
                int epPawnSquare = (movingPiece <= Piece.WhiteKing)
                    ? move.ToSquare - Globals.NORTH
                    : move.ToSquare - Globals.SOUTH;

                Piece epPawn = _squares[epPawnSquare];
                TogglePiece(epPawn, epPawnSquare);
                _squares[epPawnSquare] = Piece.None;
            }

            if (move.Flags == MOVE_FLAG_CASTLE_K || move.Flags == MOVE_FLAG_CASTLE_Q)
            {
                // Move rook with Zobrist toggles
                if (move.ToSquare == 0x06) // white king-side
                {
                    TogglePiece(Piece.WhiteRook, 0x07);
                    _squares[0x07] = Piece.None;

                    TogglePiece(Piece.WhiteRook, 0x05);
                    _squares[0x05] = Piece.WhiteRook;
                }
                else if (move.ToSquare == 0x02) // white queen-side
                {
                    TogglePiece(Piece.WhiteRook, 0x00);
                    _squares[0x00] = Piece.None;

                    TogglePiece(Piece.WhiteRook, 0x03);
                    _squares[0x03] = Piece.WhiteRook;
                }
                else if (move.ToSquare == 0x76) // black king-side
                {
                    TogglePiece(Piece.BlackRook, 0x77);
                    _squares[0x77] = Piece.None;

                    TogglePiece(Piece.BlackRook, 0x75);
                    _squares[0x75] = Piece.BlackRook;
                }
                else if (move.ToSquare == 0x72) // black queen-side
                {
                    TogglePiece(Piece.BlackRook, 0x70);
                    _squares[0x70] = Piece.None;

                    TogglePiece(Piece.BlackRook, 0x73);
                    _squares[0x73] = Piece.BlackRook;
                }
            }

            // --- Update clocks ---
            if (movingPiece == Piece.WhitePawn || movingPiece == Piece.BlackPawn || capturedPiece != Piece.None)
            {
                _halfmoveClock = 0;
            }
            else
            {
                _halfmoveClock++;
            }

            if (_sideToMove == Color.Black)
            {
                _fullmoveNumber++;
            }

            // --- Switch side ---
            ToggleSideToMove();
            _sideToMove = (_sideToMove == Color.White) ? Color.Black : Color.White;
        }






        public void UnmakeMove(Move move)
        {
            // --- Switch side back ---
            _sideToMove = (_sideToMove == Color.White) ? Color.Black : Color.White;

            if (_sideToMove == Color.Black)
            {
                _fullmoveNumber--;
            }

            // --- Restore history ---
            _historyPly--;
            _castlingRights = _castlingHistory[_historyPly];
            _enPassantSquare = _enPassantHistory[_historyPly];
            _halfmoveClock = _halfmoveHistory[_historyPly];
            Piece capturedPiece = _capturedHistory[_historyPly];
            _currentZobrist = _zobristHistory[_historyPly];
            _pawnOnlyZobrist = _pawnZobristHistory[_historyPly];

            Piece movingPiece = _squares[move.ToSquare];

            // --- Undo promotions ---
            if (move.PromotionPiece != Piece.None)
            {
                // Restore pawn
                movingPiece = (_sideToMove == Color.White) ? Piece.WhitePawn : Piece.BlackPawn;
            }

            // --- Move piece back ---
            _squares[move.FromSquare] = movingPiece;
            _squares[move.ToSquare] = capturedPiece;

            if (movingPiece == Piece.WhiteKing)
            {
                _whiteKingSquare = move.FromSquare;
            }
            else if (movingPiece == Piece.BlackKing)
            {
                _blackKingSquare = move.FromSquare;
            }

            // --- Undo en passant ---
            if (move.Flags == MOVE_FLAG_EP_CAPTURE)
            {
                int epPawnSquare = (_sideToMove == Color.White)
                    ? move.ToSquare - Globals.NORTH
                    : move.ToSquare - Globals.SOUTH;

                _squares[move.ToSquare] = Piece.None; // clear target square
                _squares[epPawnSquare] = (_sideToMove == Color.White)
                    ? Piece.BlackPawn
                    : Piece.WhitePawn;
            }

            // --- Undo castling ---
            if (move.Flags == MOVE_FLAG_CASTLE_K || move.Flags == MOVE_FLAG_CASTLE_Q)
            {
                if (move.ToSquare == 0x06) // white king-side
                {
                    _squares[0x05] = Piece.None;
                    _squares[0x07] = Piece.WhiteRook;
                }
                else if (move.ToSquare == 0x02) // white queen-side
                {
                    _squares[0x03] = Piece.None;
                    _squares[0x00] = Piece.WhiteRook;
                }
                else if (move.ToSquare == 0x76) // black king-side
                {
                    _squares[0x75] = Piece.None;
                    _squares[0x77] = Piece.BlackRook;
                }
                else if (move.ToSquare == 0x72) // black queen-side
                {
                    _squares[0x73] = Piece.None;
                    _squares[0x70] = Piece.BlackRook;
                }
            }
        }


        public bool IsInCheck(Color color)
        {
            int kingSquare = (color == Color.White) ? _whiteKingSquare : _blackKingSquare;
            Color opponent = (color == Color.White) ? Color.Black : Color.White;

            return SquareIsAttacked(kingSquare, opponent);
        }




        public bool SquareIsAttacked(int square, Color byColor)
        {
            // Pawn attacks
            if (byColor == Color.White)
            {
                int ne = square + Globals.SOUTH_WEST; 
                int nw = square + Globals.SOUTH_EAST;
                if ((ne & 0x88) == 0 && _squares[ne] == Piece.WhitePawn) 
                { 
                    return true; 
                }
                if ((nw & 0x88) == 0 && _squares[nw] == Piece.WhitePawn) 
                { 
                    return true; 
                }
            }
            else
            {
                int se = square + Globals.NORTH_WEST;
                int sw = square + Globals.NORTH_EAST;
                if ((se & 0x88) == 0 && _squares[se] == Piece.BlackPawn) 
                { 
                    return true; 
                }
                if ((sw & 0x88) == 0 && _squares[sw] == Piece.BlackPawn) 
                { 
                    return true; 
                }
            }

            // Knight attacks
            for (int nn = 0; nn < Globals.KNIGHT_DELTAS.Length; nn++)
            {
                int toSquare = square + Globals.KNIGHT_DELTAS[nn];
                if ((toSquare & 0x88) != 0)
                {
                    continue;
                }

                Piece target = _squares[toSquare];
                if (byColor == Color.White && target == Piece.WhiteKnight) 
                { 
                    return true; 
                }
                if (byColor == Color.Black && target == Piece.BlackKnight) 
                { 
                    return true; 
                }
            }

            // King adjacency (needed for castling safety and illegal king adjacency)
            for (int nn = 0; nn < Globals.KING_DELTAS.Length; nn++)
            {
                int toSquare = square + Globals.KING_DELTAS[nn];
                if ((toSquare & 0x88) != 0)
                {
                    continue;
                }

                Piece target = _squares[toSquare];
                if (byColor == Color.White && target == Piece.WhiteKing) 
                { 
                    return true; 
                }
                if (byColor == Color.Black && target == Piece.BlackKing) 
                { 
                    return true; 
                }
            }

            // Sliding attacks: bishops/queens on diagonals
            for (int nn = 0; nn < Globals.BISHOP_DIRECTIONS.Length; nn++)
            {
                int dir = Globals.BISHOP_DIRECTIONS[nn];
                int currentSquare = square + dir;

                while ((currentSquare & 0x88) == 0)
                {
                    Piece target = _squares[currentSquare];

                    if (target != Piece.None)
                    {
                        if (byColor == Color.White)
                        {
                            if (target == Piece.WhiteBishop || target == Piece.WhiteQueen) 
                            { 
                                return true; 
                            }
                            // Blocked by any piece
                            break;
                        }
                        else
                        {
                            if (target == Piece.BlackBishop || target == Piece.BlackQueen) 
                            { 
                                return true; 
                            }
                            break;
                        }
                    }

                    currentSquare += dir;
                }
            }

            // Sliding attacks: rooks/queens on ranks/files
            for (int nn = 0; nn < Globals.ROOK_DIRECTIONS.Length; nn++)
            {
                int dir = Globals.ROOK_DIRECTIONS[nn];
                int currentSquare = square + dir;

                while ((currentSquare & 0x88) == 0)
                {
                    Piece target = _squares[currentSquare];

                    if (target != Piece.None)
                    {
                        if (byColor == Color.White)
                        {
                            if (target == Piece.WhiteRook || target == Piece.WhiteQueen) 
                            { 
                                return true; 
                            }
                            break;
                        }
                        else
                        {
                            if (target == Piece.BlackRook || target == Piece.BlackQueen) 
                            { 
                                return true; 
                            }
                            break;
                        }
                    }

                    currentSquare += dir;
                }
            }

            return false;
        }

        public void GenerateAllCaptures(int ply)
        {
            _generatorPly = ply;
            _generatedMoveCount[ply] = 0; // reset buffer

            for (int nn = 0; nn <= 127; nn++)
            {
                if ((nn & 0x88) == 0)
                {
                    if (_squares[nn] == Piece.None)
                    {
                        continue;
                    }

                    Piece piece = _squares[nn];

                    // Skip pieces not belonging to side to move
                    if (piece <= Piece.WhiteKing && _sideToMove == Color.Black)
                    {
                        continue;
                    }
                    else if (piece > Piece.WhiteKing && _sideToMove == Color.White)
                    {
                        continue;
                    }

                    switch (piece)
                    {
                        case Piece.WhitePawn:
                        case Piece.BlackPawn:
                            GeneratePawnCaptures(nn);
                            break;

                        case Piece.WhiteKnight:
                        case Piece.BlackKnight:
                            GenerateKnightCaptures(nn);
                            break;

                        case Piece.WhiteBishop:
                        case Piece.BlackBishop:
                            GenerateBishopCaptures(nn);
                            break;

                        case Piece.WhiteRook:
                        case Piece.BlackRook:
                            GenerateRookCaptures(nn);
                            break;

                        case Piece.WhiteQueen:
                        case Piece.BlackQueen:
                            // Queens combine rook + bishop captures
                            GenerateRookCaptures(nn);
                            GenerateBishopCaptures(nn);
                            break;

                        case Piece.WhiteKing:
                        case Piece.BlackKing:
                            GenerateKingCaptures(nn);
                            break;

                        default:
                            // No moves for Piece.None
                            break;
                    }
                }
            }
        }




        public void GenerateAllMoves(int ply)
        {
            _generatorPly = ply;
            _generatedMoveCount[ply] = 0; // reset buffer

            for (int nn = 0; nn <= 127; nn++)
            {
                if ((nn & 0x88) == 0)
                {
                    if (_squares[nn] == Piece.None)
                    {
                        continue;
                    }

                    Piece piece = _squares[nn];

                    if (piece <= Piece.WhiteKing && _sideToMove == Color.Black)
                    {
                        continue;
                    }
                    else if (piece > Piece.WhiteKing && _sideToMove == Color.White)
                    {
                        continue;
                    }                  

                    switch (piece)
                    {
                        case Piece.WhitePawn:
                        case Piece.BlackPawn:
                            GeneratePawnMoves(nn);
                            break;

                        case Piece.WhiteKnight:
                        case Piece.BlackKnight:
                            GenerateKnightMoves(nn);
                            break;

                        case Piece.WhiteBishop:
                        case Piece.BlackBishop:
                            GenerateBishopMoves(nn);
                            break;

                        case Piece.WhiteRook:
                        case Piece.BlackRook:
                            GenerateRookMoves(nn);
                            break;

                        case Piece.WhiteQueen:
                        case Piece.BlackQueen:
                            // Queens combine rook + bishop directions
                            GenerateRookMoves(nn);
                            GenerateBishopMoves(nn);
                            break;

                        case Piece.WhiteKing:
                        case Piece.BlackKing:
                            GenerateKingMoves(nn);
                            break;

                        default:
                            // No moves for Piece.None
                            break;
                    }
                }
            }
        }

        private void GeneratePawnCaptures(int fromSquare)
        {
            Piece pawn = _squares[fromSquare];
            Color color = pawn <= Piece.WhiteKing ? Color.White : Color.Black;

            int promotionRank = (color == Color.White) ? 7 : 0;

            // --- Capture directions ---
            int[] captureDeltas = (color == Color.White)
                ? new int[] { NORTH_EAST, NORTH_WEST }
                : new int[] { SOUTH_EAST, SOUTH_WEST };

            for (int nn = 0; nn < captureDeltas.Length; nn++)
            {
                int toSquare = fromSquare + captureDeltas[nn];
                if ((toSquare & 0x88) != 0)
                {
                    continue; // offboard
                }

                Piece target = _squares[toSquare];
                if (target != Piece.None)
                {
                    bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                 || (color == Color.Black && target <= Piece.WhiteKing);

                    if (isEnemy)
                    {
                        if ((toSquare >> 4) == promotionRank)
                        {
                            // Promotion captures
                            AddPromotionMove(fromSquare, toSquare, Piece.WhiteQueen, color, target);
                            AddPromotionMove(fromSquare, toSquare, Piece.WhiteRook, color, target);
                            AddPromotionMove(fromSquare, toSquare, Piece.WhiteBishop, color, target);
                            AddPromotionMove(fromSquare, toSquare, Piece.WhiteKnight, color, target);
                        }
                        else
                        {
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                            _generatedMoveCount[_generatorPly]++;
                        }
                    }
                }
                else if (toSquare == _enPassantSquare)
                {
                    // En passant capture
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = (color == Color.White)
                        ? Piece.BlackPawn
                        : Piece.WhitePawn;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_EP_CAPTURE;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                    _generatedMoveCount[_generatorPly]++;
                }
            }
        }




        private void GeneratePawnMoves(int fromSquare)
        {
            Piece pawn = _squares[fromSquare];
            Color color = pawn <= Piece.WhiteKing ? Color.White : Color.Black;

            // Direction depends on side
            int forward = (color == Color.White) ? NORTH : SOUTH;
            int startRank = (color == Color.White) ? 1 : 6;   // rank index in 0x88 (0-based)
            int promotionRank = (color == Color.White) ? 7 : 0;

            // --- Single push ---
            int oneForward = fromSquare + forward;
            if ((oneForward & 0x88) == 0 && _squares[oneForward] == Piece.None)
            {
                if ((oneForward >> 4) == promotionRank)
                {
                    // Promotion moves
                    AddPromotionMove(fromSquare, oneForward, Piece.WhiteQueen, color);
                    AddPromotionMove(fromSquare, oneForward, Piece.WhiteRook, color);
                    AddPromotionMove(fromSquare, oneForward, Piece.WhiteBishop, color);
                    AddPromotionMove(fromSquare, oneForward, Piece.WhiteKnight, color);
                }
                else
                {
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)oneForward;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                    _generatedMoveCount[_generatorPly]++;
                }

                // --- Double push ---
                int rank = fromSquare >> 4;
                if (rank == startRank)
                {
                    int twoForward = oneForward + forward;
                    if ((twoForward & 0x88) == 0 && _squares[twoForward] == Piece.None)
                    {
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)twoForward;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_DOUBLE_PAWN_PUSH; // flag for double pawn push
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                        _generatedMoveCount[_generatorPly]++;
                    }
                }
            }

            // --- Captures (diagonals) ---
            int[] captureDeltas = (color == Color.White)
                ? new int[] { NORTH_EAST, NORTH_WEST }
                : new int[] { SOUTH_EAST, SOUTH_WEST };

            for (int nn = 0; nn < captureDeltas.Length; nn++)
            {
                int toSquare = fromSquare + captureDeltas[nn];
                if ((toSquare & 0x88) != 0)
                {
                    continue; // offboard
                }

                Piece target = _squares[toSquare];
                if (target != Piece.None)
                {
                    bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                 || (color == Color.Black && target <= Piece.WhiteKing);

                    if (isEnemy)
                    {
                        if ((toSquare >> 4) == promotionRank)
                        {
                            // Promotion captures
                            AddPromotionMove(fromSquare, toSquare, Piece.WhiteQueen, color, target);
                            AddPromotionMove(fromSquare, toSquare, Piece.WhiteRook, color, target);
                            AddPromotionMove(fromSquare, toSquare, Piece.WhiteBishop, color, target);
                            AddPromotionMove(fromSquare, toSquare, Piece.WhiteKnight, color, target);
                        }
                        else
                        {
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                            _generatedMoveCount[_generatorPly]++;
                        }
                    }
                }
                else if (toSquare == _enPassantSquare)
                {
                    // En passant capture
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = (color == Color.White)
                        ? Piece.BlackPawn
                        : Piece.WhitePawn;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_EP_CAPTURE; // flag for en passant
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                    _generatedMoveCount[_generatorPly]++;
                }
            }
        }

        /// <summary>
        /// Helper to add promotion moves (quiet or capture).
        /// </summary>
        private void AddPromotionMove(int fromSquare, int toSquare, Piece promoPiece, Color color, Piece captured = Piece.None)
        {
            // Adjust promoPiece to correct side
            if (color == Color.Black)
            {
                switch (promoPiece)
                {
                    case Piece.WhiteQueen: promoPiece = Piece.BlackQueen; break;
                    case Piece.WhiteRook: promoPiece = Piece.BlackRook; break;
                    case Piece.WhiteBishop: promoPiece = Piece.BlackBishop; break;
                    case Piece.WhiteKnight: promoPiece = Piece.BlackKnight; break;
                }
            }

            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = captured;
            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = promoPiece;
            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_PROMOTION; // flag for promotion
            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
            _generatedMoveCount[_generatorPly]++;
        }


        private void GenerateKingCaptures(int fromSquare)
        {
            Piece king = _squares[fromSquare];
            Color color = king <= Piece.WhiteKing ? Color.White : Color.Black;

            // King captures only
            for (int nn = 0; nn < KING_DELTAS.Length; nn++)
            {
                int toSquare = fromSquare + KING_DELTAS[nn];

                if ((toSquare & 0x88) != 0)
                {
                    continue; // offboard
                }

                Piece target = _squares[toSquare];

                if (target != Piece.None)
                {
                    bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                || (color == Color.Black && target <= Piece.WhiteKing);

                    if (isEnemy)
                    {
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                        _generatedMoveCount[_generatorPly]++;
                    }
                }
            }
        }




        private void GenerateKingMoves(int fromSquare)
        {
            Piece king = _squares[fromSquare];
            Color color = king <= Piece.WhiteKing ? Color.White : Color.Black;

            // Normal king moves
            for (int nn = 0; nn < KING_DELTAS.Length; nn++)
            {
                int toSquare = fromSquare + KING_DELTAS[nn];

                if ((toSquare & 0x88) != 0)
                {
                    continue; // offboard
                }

                Piece target = _squares[toSquare];

                if (target == Piece.None)
                {
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                    _generatedMoveCount[_generatorPly]++;
                }
                else
                {
                    bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                 || (color == Color.Black && target <= Piece.WhiteKing);

                    if (isEnemy)
                    {
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                        _generatedMoveCount[_generatorPly]++;
                    }
                }
            }

            // Castling (pseudo-legal: rights + empty squares only)
            if (color == Color.White)
            {
                // White king-side
                if ((_castlingRights & 1) != 0 &&
                    _squares[0x05] == Piece.None &&
                    _squares[0x06] == Piece.None)
                {
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = 0x06; // g1
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_CASTLE_K; // castling flag
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                    _generatedMoveCount[_generatorPly]++;
                }

                // White queen-side
                if ((_castlingRights & 2) != 0 &&
                    _squares[0x03] == Piece.None &&
                    _squares[0x02] == Piece.None &&
                    _squares[0x01] == Piece.None)
                {
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = 0x02; // c1
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_CASTLE_Q; // castling flag
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                    _generatedMoveCount[_generatorPly]++;
                }
            }
            else
            {
                // Black king-side
                if ((_castlingRights & 4) != 0 &&
                    _squares[0x75] == Piece.None &&
                    _squares[0x76] == Piece.None)
                {
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = 0x76; // g8
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_CASTLE_K; // castling flag
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                    _generatedMoveCount[_generatorPly]++;
                }

                // Black queen-side
                if ((_castlingRights & 8) != 0 &&
                    _squares[0x73] == Piece.None &&
                    _squares[0x72] == Piece.None &&
                    _squares[0x71] == Piece.None)
                {
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = 0x72; // c8
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_CASTLE_Q; // castling flag
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                    _generatedMoveCount[_generatorPly]++;
                }
            }
        }


        private void GenerateKnightCaptures(int fromSquare)
        {
            Piece knight = _squares[fromSquare];
            Color color = knight <= Piece.WhiteKing ? Color.White : Color.Black;

            for (int nn = 0; nn < KNIGHT_DELTAS.Length; nn++)
            {
                int toSquare = fromSquare + KNIGHT_DELTAS[nn];

                if ((toSquare & 0x88) != 0)
                {
                    continue; // offboard
                }

                Piece target = _squares[toSquare];

                // Only generate captures
                if (target != Piece.None)
                {
                    bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                || (color == Color.Black && target <= Piece.WhiteKing);

                    if (isEnemy)
                    {
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                        _generatedMoveCount[_generatorPly]++;
                    }
                }
            }
        }




        private void GenerateKnightMoves(int fromSquare)
        {
            Piece knight = _squares[fromSquare];
            Color color = knight <= Piece.WhiteKing ? Color.White : Color.Black;

            for (int nn = 0; nn < KNIGHT_DELTAS.Length; nn++)
            {
                int toSquare = fromSquare + KNIGHT_DELTAS[nn];

                if ((toSquare & 0x88) != 0)
                {
                    continue; // offboard
                }

                Piece target = _squares[toSquare];

                if (target == Piece.None)
                {
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                    _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                    _generatedMoveCount[_generatorPly]++;
                }
                else
                {
                    bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                 || (color == Color.Black && target <= Piece.WhiteKing);

                    if (isEnemy)
                    {
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                        _generatedMoveCount[_generatorPly]++;
                    }
                }
            }
        }

        private void GenerateRookCaptures(int fromSquare)
        {
            Piece rook = _squares[fromSquare];
            Color color = rook <= Piece.WhiteKing ? Color.White : Color.Black;

            for (int nn = 0; nn < ROOK_DIRECTIONS.Length; nn++)
            {
                int delta = ROOK_DIRECTIONS[nn];
                int toSquare = fromSquare + delta;

                while ((toSquare & 0x88) == 0)
                {
                    Piece target = _squares[toSquare];

                    if (target != Piece.None)
                    {
                        bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                     || (color == Color.Black && target <= Piece.WhiteKing);

                        if (isEnemy)
                        {
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                            _generatedMoveCount[_generatorPly]++;
                        }

                        break; // stop sliding in this direction after any occupied square
                    }

                    toSquare += delta;
                }
            }
        }




        private void GenerateRookMoves(int fromSquare)
        {
            Piece rook = _squares[fromSquare];
            Color color = rook <= Piece.WhiteKing ? Color.White : Color.Black;

            for (int nn = 0; nn < ROOK_DIRECTIONS.Length; nn++)
            {
                int delta = ROOK_DIRECTIONS[nn];
                int toSquare = fromSquare + delta;

                while ((toSquare & 0x88) == 0)
                {
                    Piece target = _squares[toSquare];

                    if (target == Piece.None)
                    {
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                        _generatedMoveCount[_generatorPly]++;
                    }
                    else
                    {
                        bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                     || (color == Color.Black && target <= Piece.WhiteKing);

                        if (isEnemy)
                        {
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                            _generatedMoveCount[_generatorPly]++;
                        }

                        break; // stop sliding in this direction
                    }

                    toSquare += delta;
                }
            }
        }


        private void GenerateBishopCaptures(int fromSquare)
        {
            Piece bishop = _squares[fromSquare];
            Color color = bishop <= Piece.WhiteKing ? Color.White : Color.Black;

            for (int nn = 0; nn < BISHOP_DIRECTIONS.Length; nn++)
            {
                int delta = BISHOP_DIRECTIONS[nn];
                int toSquare = fromSquare + delta;

                while ((toSquare & 0x88) == 0)
                {
                    Piece target = _squares[toSquare];

                    if (target != Piece.None)
                    {
                        bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                     || (color == Color.Black && target <= Piece.WhiteKing);

                        if (isEnemy)
                        {
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                            _generatedMoveCount[_generatorPly]++;
                        }

                        break; // stop sliding in this direction after any occupied square
                    }

                    toSquare += delta;
                }
            }
        }




        private void GenerateBishopMoves(int fromSquare)
        {
            Piece bishop = _squares[fromSquare];
            Color color = bishop <= Piece.WhiteKing ? Color.White : Color.Black;

            for (int nn = 0; nn < BISHOP_DIRECTIONS.Length; nn++)
            {
                int delta = BISHOP_DIRECTIONS[nn];
                int toSquare = fromSquare + delta;

                while ((toSquare & 0x88) == 0)
                {
                    Piece target = _squares[toSquare];

                    if (target == Piece.None)
                    {
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                        _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                        _generatedMoveCount[_generatorPly]++;
                    }
                    else
                    {
                        bool isEnemy = (color == Color.White && target > Piece.WhiteKing)
                                     || (color == Color.Black && target <= Piece.WhiteKing);

                        if (isEnemy)
                        {
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].FromSquare = (byte)fromSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].ToSquare = (byte)toSquare;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].CapturedPiece = target;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].PromotionPiece = Piece.None;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].Flags = MOVE_FLAG_NONE;
                            _generatedMoves[_generatorPly][_generatedMoveCount[_generatorPly]].MovingPiece = _squares[fromSquare];
                            _generatedMoveCount[_generatorPly]++;
                        }

                        break;
                    }

                    toSquare += delta;
                }
            }
        }
    }
}


