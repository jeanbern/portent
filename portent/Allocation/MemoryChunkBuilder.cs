using System.Runtime.CompilerServices;

namespace portent
{
    internal sealed class MemoryChunkBuilder
    {
        private ulong _count;

        public MemoryChunkBuilder ReserveAligned<T>(T[] array) where T : unmanaged
        {
            return ReserveAligned((ulong)(array.Length * Unsafe.SizeOf<T>()));
        }

        public MemoryChunkBuilder ReserveAligned(ulong length)
        {
            var necessaryOffset = MemoryAlignmentHelper.RequiredOffset(0, _count, length);
            _count += length + necessaryOffset;
            return this;
        }

        public LargePageMemoryChunk Allocate()
        {
            return new LargePageMemoryChunk(_count);
        }
    }
}
