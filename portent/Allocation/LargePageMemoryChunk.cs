using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace portent
{
    /// <summary>
    /// Helper class for allocating a large contiguous region of memory.
    /// The memory region is freed upon disposal of this object.
    /// </summary>
    /// <remarks>
    /// Sets up appropriate privileges if possible, defaulting to VirtualAlloc without large-page support if necessary.
    /// </remarks>
    /// <see cref="https://docs.microsoft.com/en-us/windows/win32/memory/large-page-support"/>
    internal sealed class LargePageMemoryChunk : IDisposable
    {
        private readonly IntPtr _ptr;
        private readonly long _bytesReserved;

        private long _offset;
        private bool _locked;

        public bool Lock()
        {
            if (!_locked)
            {
                var reservedAsPointer = new IntPtr(_bytesReserved);
                var result = NativeMethods.VirtualProtect(_ptr, reservedAsPointer, MemoryProtectionConstants.PageReadonly, out _);
                result = result && NativeMethods.VirtualLock(_ptr, reservedAsPointer);
                _locked = result;
            }

            return _locked;
        }

        /// <summary>
        /// Reserves part of the memory region for an array of the specififed type and length.
        /// </summary>
        /// <typeparam name="T">The type of the array elements.</typeparam>
        /// <param name="length">The count of items in the array.</param>
        /// <returns>A pointer to the first item in the array.</returns>
        public unsafe T* GetArray<T>(long length)
            where T : unmanaged
        {
            var lengthInBytes = Unsafe.SizeOf<T>() * length;
            Debug.Assert(_offset + lengthInBytes <= _bytesReserved);

            var result = (T*)(((byte*)_ptr) + _offset);
            _offset += lengthInBytes;

            return result;
        }

        /// <summary>
        /// Reserves part of the memory region for an array of the specififed type and length.
        /// The part reserved will be aligned on a <see cref="PageAlignmentBytes"/> byte boundary.
        /// </summary>
        /// <typeparam name="T">The type of the array elements.</typeparam>
        /// <param name="length">The count of items in the array.</param>
        /// <returns>A pointer to the first item in the array.</returns>
        public unsafe T* GetArrayAligned<T>(long length)
            where T : unmanaged
        {
            var lengthInBytes = Unsafe.SizeOf<T>() * length;
            var necessaryOffset = MemoryAlignmentHelper.RequiredOffset(_ptr, _offset);

            Debug.Assert(_offset + lengthInBytes + necessaryOffset <= _bytesReserved);

            var result = (T*)(((byte*)_ptr) + _offset + necessaryOffset);
            _offset += lengthInBytes + necessaryOffset;

            return result;
        }

        /// <summary>
        /// Reserves part of the memory region and copies into it the elements of a supplied array.
        /// </summary>
        /// <typeparam name="T">The type of the array elements.</typeparam>
        /// <param name="array">The original managed heap array.</param>
        /// <returns>A pointer to the first item in the new array.</returns>
        public unsafe T* CopyArray<T>(T[] array)
            where T : unmanaged
        {
            var lengthInBytes = Unsafe.SizeOf<T>() * array.Length;
            Debug.Assert(_offset + lengthInBytes <= _bytesReserved);

            var result = (T*)(((byte*)_ptr) + _offset);
            _offset += lengthInBytes;

            Unsafe.CopyBlockUnaligned(result, Unsafe.AsPointer(ref array[0]), (uint)lengthInBytes);

            return result;
        }

        /// <summary>
        /// Reserves part of the memory region and copies into it the elements of a supplied array.
        /// </summary>
        /// <typeparam name="T">The type of the array elements.</typeparam>
        /// <param name="array">The original managed heap array.</param>
        /// <returns>A pointer to the first item in the new array.</returns>
        public unsafe T* CopyArrayAligned<T>(T[] array)
            where T : unmanaged
        {
            var lengthInBytes = (long)Unsafe.SizeOf<T>() * array.Length;
            var necessaryOffset = MemoryAlignmentHelper.RequiredOffset(_ptr, _offset);

            Debug.Assert(_offset + lengthInBytes + necessaryOffset <= _bytesReserved);

            var result = (T*)(((byte*)_ptr) + _offset + necessaryOffset);
            _offset += lengthInBytes + necessaryOffset;

            Unsafe.CopyBlockUnaligned(result, Unsafe.AsPointer(ref array[0]), (uint)lengthInBytes);

            return result;
        }

        public LargePageMemoryChunk(long length)
        {
            const MemoryAllocationType flags = MemoryAllocationType.MemReserve | MemoryAllocationType.MemCommit;
            const string lockMemory = "SeLockMemoryPrivilege";
            _bytesReserved = MemoryAlignmentHelper.LargePageMultiple(length);

            using var privs = PrivilegeHolder.EnablePrivilege(lockMemory);
            if (privs != null)
            {
                const MemoryAllocationType largeFlag = flags | MemoryAllocationType.MemLargePages;
                _ptr = NativeMethods.VirtualAlloc(IntPtr.Zero, (IntPtr)_bytesReserved, largeFlag, MemoryProtectionConstants.PageReadwrite);
                GC.KeepAlive(privs);
            }
            else
            {
                _ptr = NativeMethods.VirtualAlloc(IntPtr.Zero, (IntPtr)_bytesReserved, flags, MemoryProtectionConstants.PageReadwrite);
            }
        }

        public static MemoryChunkBuilder Builder()
        {
            return new MemoryChunkBuilder();
        }

        ~LargePageMemoryChunk()
        {
            DisposeUnmanaged();
        }

        public void Dispose()
        {
            DisposeUnmanaged();
            GC.SuppressFinalize(this);
        }

        private int _disposed;

        private void DisposeUnmanaged()
        {
            if (Interlocked.Increment(ref _disposed) == 1)
            {
                if (_locked)
                {
                    var reservedAsPointer = new IntPtr(_bytesReserved);
                    NativeMethods.VirtualProtect(_ptr, reservedAsPointer, MemoryProtectionConstants.PageReadwrite, out _);
                    NativeMethods.VirtualUnlock(_ptr, reservedAsPointer);
                }

                NativeMethods.VirtualFree(_ptr, IntPtr.Zero, MemoryFreeType.MemRelease);
            }
        }

        private static class NativeMethods
        {
            /// <summary>
            /// Changes the protection on a region of committed pages in the virtual address space of the calling process.
            /// </summary>
            /// <param name="lpAddress">
            /// A pointer an address that describes the starting page of the region of pages whose access protection attributes are to be changed.
            /// All pages in the specified region must be within the same reserved region allocated when calling the VirtualAlloc or VirtualAllocEx function using MEM_RESERVE.
            /// The pages cannot span adjacent reserved regions that were allocated by separate calls to VirtualAlloc or VirtualAllocEx using MEM_RESERVE.
            /// </param>
            /// <param name="dwSize">
            /// The size of the region whose access protection attributes are to be changed, in bytes.
            /// The region of affected pages includes all pages containing one or more bytes in the range from the lpAddress parameter to (lpAddress+dwSize).
            /// This means that a 2-byte range straddling a page boundary causes the protection attributes of both pages to be changed.
            /// </param>
            /// <param name="flProtect">
            /// The memory protection option.
            /// This parameter can be one of the memory protection constants.
            /// For mapped views, this value must be compatible with the access protection specified when the view was mapped(see MapViewOfFile, MapViewOfFileEx, and MapViewOfFileExNuma).
            /// </param>
            /// <param name="lpflOldProtect">
            /// A pointer to a variable that receives the previous access protection value of the first page in the specified region of pages.
            /// If this parameter is NULL or does not point to a valid variable, the function fails.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is zero.To get extended error information, call GetLastError.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualprotect"/>
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool VirtualProtect(IntPtr lpAddress, IntPtr dwSize, MemoryProtectionConstants flProtect, out MemoryProtectionConstants lpflOldProtect);

            /// <summary>
            /// Locks the specified region of the process's virtual address space into physical memory, ensuring that subsequent access to the region will not incur a page fault.
            /// </summary>
            /// <param name="lpAddress">
            /// A pointer to the base address of the region of pages to be locked.
            /// </param>
            /// <param name="dwSize">
            /// The size of the region to be locked, in bytes.
            /// The region of affected pages includes all pages that contain one or more bytes in the range from the lpAddress parameter to (lpAddress+dwSize).
            /// This means that a 2-byte range straddling a page boundary causes both pages to be locked.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is zero.To get extended error information, call GetLastError.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtuallock"/>
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool VirtualLock(IntPtr lpAddress, IntPtr dwSize);

            /// <summary>
            /// Unlocks a specified range of pages in the virtual address space of a process, enabling the system to swap the pages out to the paging file if necessary.
            /// </summary>
            /// <param name="lpAddress">
            /// A pointer to the base address of the region of pages to be unlocked.
            /// </param>
            /// <param name="dwSize">
            /// The size of the region being unlocked, in bytes.
            /// The region of affected pages includes all pages containing one or more bytes in the range from the lpAddress parameter to (lpAddress+dwSize).
            /// This means that a 2-byte range straddling a page boundary causes both pages to be unlocked.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is zero. To get extended error information, call GetLastError.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualunlock"/>
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool VirtualUnlock(IntPtr lpAddress, IntPtr dwSize);

            /// <summary>
            /// Reserves, commits, or changes the state of a region of pages in the virtual address space of the calling process.
            /// Memory allocated by this function is automatically initialized to zero.
            /// </summary>
            /// <param name="lpAddress" type="LPVOID">
            /// The starting address of the region to allocate.
            /// If the memory is being reserved, the specified address is rounded down to the nearest multiple of the allocation granularity.
            /// If the memory is already reserved and is being committed, the address is rounded down to the next page boundary.
            /// To determine the size of a page and the allocation granularity on the host computer, use the GetSystemInfo function.
            /// If this parameter is NULL, the system determines where to allocate the region.
            /// </param>
            /// <param name="dwSize">
            /// The size of the region, in bytes.
            /// If the lpAddress parameter is NULL, this value is rounded up to the next page boundary.
            /// Otherwise, the allocated pages include all pages containing one or more bytes in the range from lpAddress to lpAddress+dwSize.
            /// This means that a 2-byte range straddling a page boundary causes both pages to be included in the allocated region.
            /// </param>
            /// <param name="flAllocationType">
            /// The type of memory allocation.
            /// </param>
            /// <param name="flProtect">The memory protection for the region of pages to be allocated.
            /// If the pages are being committed, you can specify any one of the memory protection constants.
            /// If lpAddress specifies an address within an enclave, flProtect cannot be any of the following values:
            ///     PAGE_NOACCESS
            ///     PAGE_GUARD
            ///     PAGE_NOCACHE
            ///     PAGE_WRITECOMBINE
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is the base address of the allocated region of pages.
            /// If the function fails, the return value is NULL.To get extended error information, call GetLastError.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualalloc"/>
            [DllImport("kernel32.dll", EntryPoint = "VirtualAlloc", SetLastError = true)]
            private static extern IntPtr VirtualAllocInterop(IntPtr lpAddress, IntPtr dwSize, MemoryAllocationType flAllocationType, MemoryProtectionConstants flProtect);

            /// <summary>
            /// Reserves, commits, or changes the state of a region of pages in the virtual address space of the calling process.
            /// Memory allocated by this function is automatically initialized to zero.
            /// </summary>
            /// <param name="lpAddress" type="LPVOID">
            /// The starting address of the region to allocate.
            /// If the memory is being reserved, the specified address is rounded down to the nearest multiple of the allocation granularity.
            /// If the memory is already reserved and is being committed, the address is rounded down to the next page boundary.
            /// To determine the size of a page and the allocation granularity on the host computer, use the GetSystemInfo function.
            /// If this parameter is NULL, the system determines where to allocate the region.
            /// </param>
            /// <param name="dwSize">
            /// The size of the region, in bytes.
            /// If the lpAddress parameter is NULL, this value is rounded up to the next page boundary.
            /// Otherwise, the allocated pages include all pages containing one or more bytes in the range from lpAddress to lpAddress+dwSize.
            /// This means that a 2-byte range straddling a page boundary causes both pages to be included in the allocated region.
            /// </param>
            /// <param name="flAllocationType">
            /// The type of memory allocation.
            /// </param>
            /// <param name="flProtect">The memory protection for the region of pages to be allocated.
            /// If the pages are being committed, you can specify any one of the memory protection constants.
            /// If lpAddress specifies an address within an enclave, flProtect cannot be any of the following values:
            ///     PAGE_NOACCESS
            ///     PAGE_GUARD
            ///     PAGE_NOCACHE
            ///     PAGE_WRITECOMBINE
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is the base address of the allocated region of pages.
            /// If the function fails, the return value is NULL.To get extended error information, call GetLastError.
            /// </returns>
            public static IntPtr VirtualAlloc(IntPtr lpAddress, IntPtr dwSize, MemoryAllocationType flAllocationType, MemoryProtectionConstants flProtect)
            {
                const string flagNotSupported = "Flag not supported by the VirtualAlloc or VirtualAllocEx functions: ";
                if ((flProtect & MemoryProtectionConstants.PageExecuteReadwrite) != 0)
                {
                    throw new InvalidOperationException(flagNotSupported + nameof(MemoryProtectionConstants.PageExecuteReadwrite));
                }

                if ((flProtect & MemoryProtectionConstants.PageWritecopy) != 0)
                {
                    throw new InvalidOperationException(flagNotSupported + nameof(MemoryProtectionConstants.PageWritecopy));
                }

                if ((flAllocationType & MemoryAllocationType.MemLargePages) != 0)
                {
                    const string specifyLargeMustAlso = "If you specify " + nameof(MemoryAllocationType.MemLargePages) + "you must also specify: ";
                    if ((flAllocationType & MemoryAllocationType.MemReserve) == 0)
                    {
                        throw new InvalidOperationException(specifyLargeMustAlso + nameof(MemoryAllocationType.MemReserve));
                    }
                    if ((flAllocationType & MemoryAllocationType.MemCommit) == 0)
                    {
                        throw new InvalidOperationException(specifyLargeMustAlso + nameof(MemoryAllocationType.MemCommit));
                    }
                }

                if ((flAllocationType & MemoryAllocationType.MemPhysical) != 0 && (flAllocationType ^ MemoryAllocationType.MemReserve) != 0)
                {
                    throw new InvalidOperationException(nameof(MemoryAllocationType.MemPhysical) + " must be used with " + nameof(MemoryAllocationType.MemReserve) + " and no other values");
                }

                if ((flAllocationType & MemoryAllocationType.MemWriteWatch) != 0 && (flAllocationType & MemoryAllocationType.MemReserve) == 0)
                {
                    throw new InvalidOperationException("If you specify " + nameof(MemoryAllocationType.MemWriteWatch) + ", you must also specify " + nameof(MemoryAllocationType.MemReserve));
                }

                if ((flAllocationType & MemoryAllocationType.MemResetUndo) != 0 && flAllocationType != MemoryAllocationType.MemResetUndo)
                {
                    throw new InvalidOperationException(nameof(MemoryAllocationType.MemResetUndo) + " cannot be used with any other value.");
                }

                return VirtualAllocInterop(lpAddress, dwSize, flAllocationType, flProtect);
            }

            /// <summary>
            /// Releases, decommits, or releases and decommits a region of pages within the virtual address space of the calling process.
            /// </summary>
            /// <param name="lpAddress">
            /// A pointer to the base address of the region of pages to be freed.
            /// If the dwFreeType parameter is MEM_RELEASE, this parameter must be the base address returned by the VirtualAlloc function when the region of pages is reserved.
            /// </param>
            /// <param name="dwSize">
            /// The size of the region of memory to be freed, in bytes.
            /// If the dwFreeType parameter is MEM_RELEASE, this parameter must be 0 (zero).
            /// The function frees the entire region that is reserved in the initial allocation call to VirtualAlloc.
            /// If the dwFreeType parameter is MEM_DECOMMIT, the function decommits all memory pages that contain one or more bytes in the range from the lpAddress parameter to (lpAddress+dwSize).
            /// This means, for example, that a 2-byte region of memory that straddles a page boundary causes both pages to be decommitted.
            /// If lpAddress is the base address returned by VirtualAlloc and dwSize is 0 (zero), the function decommits the entire region that is allocated by VirtualAlloc.
            /// After that, the entire region is in the reserved state.
            /// </param>
            /// <param name="dwFreeType">
            /// The type of free operation.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is 0 (zero). To get extended error information, call GetLastError.
            /// </returns>
            /// <see cref="https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualfree"/>
            [DllImport("kernel32.dll", EntryPoint = "VirtualFree", SetLastError = true)]
            private static extern bool VirtualFreeInterop(IntPtr lpAddress, IntPtr dwSize, MemoryFreeType dwFreeType);

            /// <summary>
            /// Releases, decommits, or releases and decommits a region of pages within the virtual address space of the calling process.
            /// </summary>
            /// <param name="lpAddress">
            /// A pointer to the base address of the region of pages to be freed.
            /// If the dwFreeType parameter is MEM_RELEASE, this parameter must be the base address returned by the VirtualAlloc function when the region of pages is reserved.
            /// </param>
            /// <param name="dwSize">
            /// The size of the region of memory to be freed, in bytes.
            /// If the dwFreeType parameter is MEM_RELEASE, this parameter must be 0 (zero).
            /// The function frees the entire region that is reserved in the initial allocation call to VirtualAlloc.
            /// If the dwFreeType parameter is MEM_DECOMMIT, the function decommits all memory pages that contain one or more bytes in the range from the lpAddress parameter to (lpAddress+dwSize).
            /// This means, for example, that a 2-byte region of memory that straddles a page boundary causes both pages to be decommitted.
            /// If lpAddress is the base address returned by VirtualAlloc and dwSize is 0 (zero), the function decommits the entire region that is allocated by VirtualAlloc.
            /// After that, the entire region is in the reserved state.
            /// </param>
            /// <param name="dwFreeType">
            /// The type of free operation.
            /// </param>
            /// <returns>
            /// If the function succeeds, the return value is nonzero.
            /// If the function fails, the return value is 0 (zero). To get extended error information, call GetLastError.
            /// </returns>
            public static void VirtualFree(IntPtr lpAddress, IntPtr dwSize, MemoryFreeType dwFreeType)
            {
                if ((dwFreeType & MemoryFreeType.MemDecommit) != 0 && (dwFreeType & MemoryFreeType.MemRelease) != 0)
                {
                    throw new InvalidOperationException("Do not use " + nameof(MemoryFreeType.MemDecommit) + " and " + nameof(MemoryFreeType.MemRelease) + " together.");
                }

                VirtualFreeInterop(lpAddress, dwSize, dwFreeType);
            }
        }
    }
}
