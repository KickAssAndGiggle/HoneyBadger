using System;

namespace HoneyBadger
{
    public static class Globals
    {
        // Direction deltas for 0x88
        public const int NORTH = 16;
        public const int SOUTH = -16;
        public const int EAST = 1;
        public const int WEST = -1;

        public const int NORTH_EAST = 17;
        public const int NORTH_WEST = 15;
        public const int SOUTH_EAST = -15;
        public const int SOUTH_WEST = -17;

        public const int MOVE_FLAG_NONE = 0;
        public const int MOVE_FLAG_CASTLE_K = 1;
        public const int MOVE_FLAG_CASTLE_Q = 2;
        public const int MOVE_FLAG_DOUBLE_PAWN_PUSH = 4;
        public const int MOVE_FLAG_EP_CAPTURE = 8;
        public const int MOVE_FLAG_PROMOTION = 16;

        
        // Knight move deltas
        public static readonly int[] KNIGHT_DELTAS =
        {
            33,
            31,
            18,
            14,
            -33,
            -31,
            -18,
            -14
        };

        // King move deltas
        public static readonly int[] KING_DELTAS =
        {
            NORTH,
            SOUTH,
            EAST,
            WEST,
            NORTH_EAST,
            NORTH_WEST,
            SOUTH_EAST,
            SOUTH_WEST
        };

        // Piece values
        public const int PAWN_VALUE = 100;
        public const int KNIGHT_VALUE = 320;
        public const int BISHOP_VALUE = 330;
        public const int ROOK_VALUE = 500;
        public const int QUEEN_VALUE = 900;
        public const int KING_VALUE = 20000;

        // Search constants (choose values consistent with your engine's scale)
        public const int CHECKMATE = 50000; // large value >> any eval; mate scoring

        // Board size
        public const int BOARD_SIZE = 128;

        public static readonly int[] BISHOP_DIRECTIONS =
        {
            NORTH_EAST,
            NORTH_WEST,
            SOUTH_EAST,
            SOUTH_WEST
        };

        public static readonly int[] ROOK_DIRECTIONS =
        {
            NORTH,
            SOUTH,
            EAST,
            WEST
        };

        public enum TTNodeType : byte
        {
            Exact,
            UpperBound, 
            LowerBound  
        }

        public struct TTEntry
        {
            public ulong Key;        
            public Move BestMove;    
            public int Score;      
            public byte Depth;       
            public TTNodeType Type;  
            public byte Age;         
        }




        public static Piece CharToPiece(char c)
        {
            switch (c)
            {
                case 'P': return Piece.WhitePawn;
                case 'N': return Piece.WhiteKnight;
                case 'B': return Piece.WhiteBishop;
                case 'R': return Piece.WhiteRook;
                case 'Q': return Piece.WhiteQueen;
                case 'K': return Piece.WhiteKing;
                case 'p': return Piece.BlackPawn;
                case 'n': return Piece.BlackKnight;
                case 'b': return Piece.BlackBishop;
                case 'r': return Piece.BlackRook;
                case 'q': return Piece.BlackQueen;
                case 'k': return Piece.BlackKing;
                default: return Piece.None;
            }
        }

        public struct Move : IComparable<Move>
        {
            public byte FromSquare;
            public byte ToSquare;
            public Piece MovingPiece;
            public Piece CapturedPiece;
            public Piece PromotionPiece;
            public byte Flags; // bitmask for castling, en passant, promotion, etc.
            public byte SortScore;
            public int CompareTo(Move other)
            {
                return other.SortScore.CompareTo(this.SortScore);
            }
        }

        public static string SquareToString(int square)
        {
            int file = square & 0x7;          // low nibble
            int rank = (square >> 4) & 0x7;   // high nibble
            char fileChar = (char)('a' + file);
            char rankChar = (char)('1' + rank);
            return $"{fileChar}{rankChar}";
        }



    }

    public enum Color : byte
    {
        White = 0,
        Black = 1
    }

    public enum Piece : byte
    {
        None = 0,
        WhitePawn,
        WhiteKnight,
        WhiteBishop,
        WhiteRook,
        WhiteQueen,
        WhiteKing,
        BlackPawn,
        BlackKnight,
        BlackBishop,
        BlackRook,
        BlackQueen,
        BlackKing
    }
}


