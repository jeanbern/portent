using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace portent
{
    public struct MemoryChunkBuilder
    {
        private long _count;

        public MemoryChunkBuilder ReserveUnaligned<T>(int count) where T : unmanaged
        {
            return ReserveUnaligned(count * Unsafe.SizeOf<T>());
        }

        public MemoryChunkBuilder ReserveUnaligned<T>(T[] array) where T : unmanaged
        {
            return ReserveUnaligned(array.Length * Unsafe.SizeOf<T>());
        }

        public MemoryChunkBuilder ReserveUnaligned(long length)
        {
            _count += length;
            return this;
        }

        public MemoryChunkBuilder ReserveAligned<T>(int count) where T : unmanaged
        {
            return ReserveAligned(count * Unsafe.SizeOf<T>());
        }

        public MemoryChunkBuilder ReserveAligned<T>(T[] array) where T : unmanaged
        {
            return ReserveAligned(array.Length * Unsafe.SizeOf<T>());
        }

        public MemoryChunkBuilder ReserveAligned(long length)
        {
            var necessaryOffset = (LargePageMemoryChunk.PageAlignmentBytes - (length % LargePageMemoryChunk.PageAlignmentBytes)) % LargePageMemoryChunk.PageAlignmentBytes;
            Debug.Assert((length + necessaryOffset) % LargePageMemoryChunk.PageAlignmentBytes == 0);
            _count += length + necessaryOffset;
            return this;
        }

        public unsafe LargePageMemoryChunk Allocate()
        {
            return new LargePageMemoryChunk(_count);
        }

        public override bool Equals(object? obj)
        {
            return obj is MemoryChunkBuilder builder && Equals(this, builder);
        }

        public static bool Equals(MemoryChunkBuilder builder1, MemoryChunkBuilder builder2)
        {
            return builder1._count == builder2._count;
        }

        public override int GetHashCode()
        {
            return _count.GetHashCode();
        }

        public static bool operator ==(MemoryChunkBuilder left, MemoryChunkBuilder right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(MemoryChunkBuilder left, MemoryChunkBuilder right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return _count.ToString();
        }
    }
}
