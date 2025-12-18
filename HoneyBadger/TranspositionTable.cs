using static HoneyBadger.Globals;
namespace HoneyBadger
{
    public class TranspositionTable
    {
        private readonly TTEntry[] _entries;
        private readonly int _mask; // index mask

        public TranspositionTable(int sizeMB)
        {
            int entrySize = System.Runtime.InteropServices.Marshal.SizeOf<TTEntry>();
            int entries = (sizeMB * 1024 * 1024) / entrySize;

            // Ensure power-of-two size for mask
            int pow2 = 1;
            while (pow2 < entries)
            {
                pow2 <<= 1;
            }

            _entries = new TTEntry[pow2];
            _mask = pow2 - 1;
        }

        public void Store(ulong key, Move bestMove, byte depth, int score, TTNodeType type, byte age)
        {
            int index = (int)(key & (ulong)_mask);
            _entries[index] = new TTEntry
            {
                Key = key,
                BestMove = bestMove,
                Depth = depth,
                Score = score,
                Type = type,
                Age = age
            };
        }

        public bool Probe(ulong key, out TTEntry entry)
        {
            int index = (int)(key & (ulong)_mask);
            entry = _entries[index];
            return entry.Key == key;
        }
    }


}
