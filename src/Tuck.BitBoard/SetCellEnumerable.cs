using System;
using System.Collections;
using System.Collections.Generic;

namespace Tuck.BitBoard;

/// <summary>Set-cell coordinates in row-major order. Direct foreach iteration allocates no memory.</summary>
/// <remarks>This is a live view. Do not mutate its board during enumeration. Interface enumeration may box.</remarks>
public readonly struct SetCellEnumerable : IEnumerable<(int X, int Y)>
{
    private readonly ulong[]? _storage;

    internal SetCellEnumerable(ulong[] storage) => _storage = storage;

    public Enumerator GetEnumerator() => new(_storage);
    IEnumerator<(int X, int Y)> IEnumerable<(int X, int Y)>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<(int X, int Y)>
    {
        private readonly ulong[]? _storage;
        private readonly int _columns, _height;
        private int _nextTileX, _x, _y;
        private uint _row;

        internal Enumerator(ulong[]? storage)
        {
            _storage = storage;
            _columns = storage == null ? 0 : BitBoardOperations.TileLength(BitBoardOperations.Width(storage));
            _height = _columns == 0 ? 0 : BitBoardOperations.Height(storage!);
            _nextTileX = _x = _y = 0;
            _row = 0;
            Current = default;
        }

        public (int X, int Y) Current { get; private set; }
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (_row == 0)
            {
                if (_nextTileX == _columns)
                {
                    _nextTileX = 0;
                    if (_y < _height) _y++;
                }
                if (_y >= _height) return false;
                _x = _nextTileX * 8;
                _row = (uint)((_storage![1 + (_y / 8) * _columns + _nextTileX++] >> ((_y & 7) * 8)) & 255);
            }
            // Locate the next set bit in this byte without visiting each preceding empty cell.
            int skip = 0;
            if ((_row & 1) == 0)
            {
                uint bits = _row;
                if ((bits & 15) == 0) { skip += 4; bits >>= 4; }
                if ((bits & 3) == 0) { skip += 2; bits >>= 2; }
                if ((bits & 1) == 0) skip++;
            }
            Current = (_x + skip, _y);
            _x += skip + 1;
            _row >>= skip + 1;
            return true;
        }

        public void Dispose() { }
        void IEnumerator.Reset() => throw new NotSupportedException();
    }
}
