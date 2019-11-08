using System;
using JetBrains.Annotations;

namespace portent
{
    /// <summary>
    /// The type of memory allocation for VirtualAlloc.
    /// </summary>
    /// <see>
    /// <cref>https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualalloc</cref>
    /// </see>
    [Flags]
    [PublicAPI]
    public enum MemoryAllocationType : uint
    {
        None = 0,
        MemCommit = 0x00001000,
        MemReserve = 0x00002000,
        MemReset = 0x00080000,
        MemTopDown = 0x00100000,
        MemWriteWatch = 0x00200000,
        MemPhysical = 0x00400000,
        MemResetUndo = 0x01000000,
        MemLargePages = 0x20000000
    }
}
