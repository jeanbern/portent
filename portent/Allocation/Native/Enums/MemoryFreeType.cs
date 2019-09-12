using System;

namespace portent
{
    /// <summary>
    /// The type of free operation for VirtualFree.
    /// </summary>
    /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualfree"/>
    [Flags]
    public enum MemoryFreeType
    {
        None = 0x00,
        MemCoalescePlaceholders = 0x00000001,
        MemPreservePlaceholder = 0x00000002,
        MemDecommit = 0x00004000,
        MemRelease = 0x00008000
    }
}
