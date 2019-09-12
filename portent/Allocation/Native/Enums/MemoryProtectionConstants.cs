﻿using System;

namespace portent
{
    /// <summary>
    /// The following are the memory-protection options; you must specify one of the following values when allocating or protecting a page in memory.
    /// Protection attributes cannot be assigned to a portion of a page; they can only be assigned to a whole page.
    /// </summary>
    /// <see cref="https://docs.microsoft.com/en-us/windows/win32/memory/memory-protection-constants"/>
    [Flags]
    public enum MemoryProtectionConstants : uint
    {
        None = 0,
        PageExecute = 0x10,
        PageExecuteRead = 0x20,
        PageExecuteReadwrite = 0x40,
        PageExecuteWritecopy = 0x80,
        PageNoaccess = 0x01,
        PageReadonly = 0x02,
        PageReadwrite = 0x04,
        PageWritecopy = 0x08,
        PageTargetsInvalid = 0x40000000,
        PageTargetsNoUpdate = 0x40000000,

        PageGuard = 0x100,
        PageNocache = 0x200,
        PageWritecombine = 0x400
    }
}