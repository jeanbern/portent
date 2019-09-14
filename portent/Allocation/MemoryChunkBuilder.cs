using System;
using System.Runtime.CompilerServices;

namespace portent
{
    internal sealed class MemoryChunkBuilder
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
            // TODO: Don't need to move to the next page if the current one still has room.
            // This requires tracking how much is left on the last page.
            var necessaryOffset = MemoryAlignmentHelper.RequiredOffset(IntPtr.Zero, length);
            _count += length + necessaryOffset;
            return this;
        }

        public LargePageMemoryChunk Allocate()
        {
            return new LargePageMemoryChunk(_count);
        }
    }
}
