﻿using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Portent
{
    public static class MathUtils
    {
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [JetBrains.Annotations.PublicAPI]
        public static int PositiveOrZeroNonBranching(int x)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            return x - (x & (x >> ((sizeof(int) * 8) - 1)));
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [JetBrains.Annotations.PublicAPI]
        public static long PositiveOrZeroNonBranching(long x)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            return x - (x & (x >> ((sizeof(long) * 8) - 1)));
        }

        /// <summary>
        /// Non-branching alternative to: "return value > 0 ? value : -value;"
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [JetBrains.Annotations.PublicAPI]
        public static int Abs(int value)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            var mask = value >> ((sizeof(int) * 8) - 1);
            return (value + mask) ^ mask;
        }

        /// <summary>
        /// Non-branching alternative to: "return value > 0 ? value : -value;"
        /// </summary>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [JetBrains.Annotations.PublicAPI]
        public static long Abs(long value)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            var mask = value >> ((sizeof(long) * 8) - 1);
            return (value + mask) ^ mask;
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Min(int x, int y)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            return y + ((x - y) & ((x - y) >> ((sizeof(int) * 8) - 1)));
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Min(uint x, uint y) => (uint) Min((int) x, (int) y);

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Min(long x, long y)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            return y + ((x - y) & ((x - y) >> ((sizeof(long) * 8) - 1)));
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [JetBrains.Annotations.PublicAPI]
        public static ulong Min(ulong x, ulong y) => (ulong) Min((long) x, (long) y);

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [JetBrains.Annotations.PublicAPI]
        public static uint RotateRight(uint x, int n)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            return (x >> n) | (x << ((sizeof(uint) * 8) - n));
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateRight(ulong x, int n)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            return (x >> n) | (x << ((sizeof(ulong) * 8) - n));
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [JetBrains.Annotations.PublicAPI]
        public static ulong SpecificSwap(ulong a)
        {
            return ((a & 0xffff) << 48) | (a >> 48);
        }
    }
}
