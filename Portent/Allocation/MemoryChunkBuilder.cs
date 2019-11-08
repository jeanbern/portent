using System.Runtime.CompilerServices;

namespace Portent
{
    internal sealed class MemoryChunkBuilder
    {
        public ulong AllocationSize { get; private set; }

        public MemoryChunkBuilder ReserveAligned<T>(T[] array) where T : unmanaged
        {
            return ReserveAligned((ulong)(array.Length * Unsafe.SizeOf<T>()));
        }

        public MemoryChunkBuilder ReserveAligned(ulong length)
        {
            var necessaryOffset = MemoryAlignmentHelper.RequiredOffset(0, AllocationSize, length);
            AllocationSize += length + necessaryOffset;
            return this;
        }
    }
}
