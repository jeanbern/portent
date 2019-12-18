using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Portent
{
    internal static class MemoryAlignmentHelper
    {
        public static ulong RequiredOffset(ulong start, ulong currentOffset, ulong requestedLength)
        {
            var currentLocation = start + currentOffset;
            var distanceFromPreviousBoundary = currentLocation % PageAlignmentBytes;

            if (distanceFromPreviousBoundary + requestedLength < PageAlignmentBytes)
            {
                // It might fit in the current page.
                var distanceFromPreviousCacheAlignment = distanceFromPreviousBoundary % L1CacheLineSizeBytes;
                var distanceToNextCacheAlignment = L1CacheLineSizeBytes - distanceFromPreviousCacheAlignment;
                var minimumCacheAlignmentOffset = distanceToNextCacheAlignment % L1CacheLineSizeBytes;
                if (distanceFromPreviousBoundary + minimumCacheAlignmentOffset + requestedLength < PageAlignmentBytes)
                {
                    // It does fit in the current page.
                    return 0;
                }
            }

            var distanceToNextBoundary = PageAlignmentBytes - distanceFromPreviousBoundary;

            var minimumBoundaryOffset = distanceToNextBoundary % PageAlignmentBytes;
            Debug.Assert((currentLocation + minimumBoundaryOffset) % PageAlignmentBytes == 0);

            return minimumBoundaryOffset;
        }

        public static ulong LargePageMultiple(ulong length)
        {
            var reserved = (ulong) LargePageMinimum * (Ceiling(length, (ulong) LargePageMinimum) + 1);
            Debug.Assert(reserved >= length);
            return reserved;
        }

        /// <summary>
        /// Don't trust this for values smaller than or equal to 0
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Ceiling(ulong dividend, ulong divisor)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            return 1 + ((dividend - 1) / divisor);
        }

        public static int GetCacheAlignedSize(long length)
        {
            return (int)length + (L1CacheLineSizeBytes - 1);
        }

        /// <summary>
        /// Returns the minimum length of continuous memory from which a <paramref name="length"/> sized portion beginning at a cache line boundary can be allocated.
        /// </summary>
        /// <typeparam name="T">The type of the allocated items.</typeparam>
        /// <param name="length">The number of items requiring allocation.</param>
        /// <returns>
        /// The size in bytes of the required memory allocation.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetCacheAlignedSize<T>(uint length)
            where T : unmanaged
        {
            return GetCacheAlignedSize(Unsafe.SizeOf<T>() * length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T* GetCacheAlignedStart<T>(byte* firstElement)
            where T : unmanaged
        {
            var pointer = (long)firstElement;
            var distanceFromPreviousBoundary = pointer % L1CacheLineSizeBytes;
            var distanceToNextBoundary = L1CacheLineSizeBytes - distanceFromPreviousBoundary;

            var minimumBoundaryOffset = distanceToNextBoundary % L1CacheLineSizeBytes;
            Debug.Assert((pointer + minimumBoundaryOffset) % L1CacheLineSizeBytes == 0);

            return (T*)(pointer + minimumBoundaryOffset);
        }

        /// <summary>
        /// The size in bytes of a processor cache line.
        /// </summary>
        public const int L1CacheLineSizeBytes = 64;

        private static UIntPtr _largePageMinimumBacking = UIntPtr.Zero;

        public static ulong PageAlignmentBytes => L1CacheLineSizeBytes;

        public static UIntPtr LargePageMinimum
        {
            get
            {
                if (_largePageMinimumBacking == UIntPtr.Zero)
                {
                    _largePageMinimumBacking = NativeMethods.GetLargePageMinimum();
                }

                return _largePageMinimumBacking;
            }
        }

        private static class NativeMethods
        {
            /// <summary>
            /// Retrieves the minimum size of a large page.
            /// </summary>
            /// <returns>
            /// If the processor supports large pages, the return value is the minimum size of a large page.
            /// If the processor does not support large pages, the return value is zero.
            /// </returns>
            /// <remarks>
            /// The minimum large page size varies, but it is typically 2 MB or greater. (Actually MiB)
            /// </remarks>
            /// <see>
            /// <cref>https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-getlargepageminimum</cref>
            /// </see>
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern UIntPtr GetLargePageMinimum();
        }
    }
}
