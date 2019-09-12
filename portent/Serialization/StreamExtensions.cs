using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using LocalsInit;

namespace JBP
{
    /*  This class is a snippet from https://github.com/jeanbern/jeanbern.github.io/blob/master/code/StreamExtensions.cs
     *  Copyright © 2019 Jean-Bernard Pellerin
     *  MIT License
     *  https://github.com/jeanbern/jeanbern.github.io/blob/master/LICENSE
     */
    //TODO: The compressed functions suck. Remove the length int that preceeds a run of same-lengthed items.
    public static class StreamExtensions
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write<T>(this Stream stream, T value)
            where T : unmanaged
        {
            var tSpan = MemoryMarshal.CreateSpan(ref value, 1);
            var span = MemoryMarshal.AsBytes(tSpan);
            stream.Write(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write<T>(this Stream stream, ref T value)
            where T : unmanaged
        {
            var tSpan = MemoryMarshal.CreateSpan(ref value, 1);
            var span = MemoryMarshal.AsBytes(tSpan);
            stream.Write(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write<T>(this Stream stream, T[] values)
            where T : unmanaged
        {
            var tSpan = values.AsSpan();
            var span = MemoryMarshal.AsBytes(tSpan);
            stream.Write(values.Length);
            stream.Write(span);
        }

        private static int GetCompressedSize(int value)
        {
            var length = 1;
            while (value >= 0x80)
            {
                length++;
                value >>= 7;
            }

            return length;
        }

        private static int GetCompressedSize(uint value)
        {
            var length = 1;
            while (value >= 0x80)
            {
                length++;
                value >>= 7;
            }

            return length;
        }

        private static int GetCompressedSize(long value)
        {
            var length = 1;
            while (value >= 0x80)
            {
                length++;
                value >>= 7;
            }

            return length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteCompressed(this Stream stream, Span<ushort> values)
        {
            stream.WriteCompressed(values.Length);

            var startIndex = 0;
            while (startIndex < values.Length)
            {
                var endIndex = startIndex;
                var current = values[startIndex];
                var valueSize = GetCompressedSize(current);
                endIndex++;
                while (endIndex < values.Length && valueSize == GetCompressedSize(values[endIndex]))
                {
                    endIndex++;
                }

                stream.WriteCompressed(valueSize);

                stream.WriteCompressed(endIndex - startIndex);

                while (startIndex < endIndex)
                {
                    stream.WriteCompressed(values[startIndex]);
                    startIndex++;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteCompressed(this Stream stream, Span<int> values)
        {
            stream.WriteCompressed(values.Length);

            var startIndex = 0;
            while (startIndex < values.Length)
            {
                var endIndex = startIndex;
                var current = values[startIndex];
                var valueSize = GetCompressedSize(current);
                endIndex++;
                while (endIndex < values.Length && valueSize == GetCompressedSize(values[endIndex]))
                {
                    endIndex++;
                }

                stream.WriteCompressed(valueSize);

                stream.WriteCompressed(endIndex - startIndex);

                while (startIndex < endIndex)
                {
                    stream.WriteCompressed(values[startIndex]);
                    startIndex++;
                }
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteCompressed(this Stream stream, Span<uint> values)
        {
            stream.WriteCompressed(values.Length);

            var startIndex = 0;
            while (startIndex < values.Length)
            {
                var endIndex = startIndex;
                var current = values[startIndex];
                var valueSize = GetCompressedSize(current);
                endIndex++;
                while (endIndex < values.Length && valueSize == GetCompressedSize(values[endIndex]))
                {
                    endIndex++;
                }

                stream.WriteCompressed(valueSize);

                stream.WriteCompressed(endIndex - startIndex);

                while (startIndex < endIndex)
                {
                    stream.WriteCompressed(values[startIndex]);
                    startIndex++;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteCompressed(this Stream stream, Span<long> values)
        {
            stream.WriteCompressed(values.Length);

            var startIndex = 0;
            while (startIndex < values.Length)
            {
                var endIndex = startIndex;
                var current = values[startIndex];
                var valueSize = GetCompressedSize(current);
                endIndex++;
                while (endIndex < values.Length && valueSize == GetCompressedSize(values[endIndex]))
                {
                    endIndex++;
                }

                stream.WriteCompressed(valueSize);

                stream.WriteCompressed(endIndex - startIndex);

                while (startIndex < endIndex)
                {
                    stream.WriteCompressed(values[startIndex]);
                    startIndex++;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteCompressed(this Stream stream, ushort value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteCompressed(this Stream stream, int value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteCompressed(this Stream stream, long value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadCompressedUshort(this Stream stream)
        {
            ushort value = 0;
            stream.ReadCompressed(ref value);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadCompressedInt(this Stream stream)
        {
            var value = 0;
            stream.ReadCompressed(ref value);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadCompressedLong(this Stream stream)
        {
            long value = 0;
            stream.ReadCompressed(ref value);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadCompressed(this Stream stream, ref ushort value)
        {
            var shift = 0;
            byte b;
            do
            {
                b = (byte)stream.ReadByte();
                if (shift == 9 * 7 && b > 0x01)
                {
                    Throw(new InvalidOperationException());
                    return;
                }

                value |= (ushort)((b & 0x7F) << shift);
                shift += 7;
            } while ((b & 0x80) != 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadCompressed(this Stream stream, ref int value)
        {
            var shift = 0;
            byte b;
            do
            {
                b = (byte)stream.ReadByte();
                if (shift == 9 * 7 && b > 0x01)
                {
                    value = -1;
                    return;
                }

                value |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadCompressed(this Stream stream, ref uint value)
        {
            var shift = 0;
            byte b;
            do
            {
                b = (byte)stream.ReadByte();
                if (shift == 9 * 7 && b > 0x01)
                {
                    value = uint.MaxValue;
                    return;
                }

                value |= (uint)((b & 0x7F) << shift);
                shift += 7;
            } while ((b & 0x80) != 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadCompressed(this Stream stream, ref long value)
        {
            var shift = 0;
            byte b;
            do
            {
                b = (byte)stream.ReadByte();
                if (shift == 9 * 7 && b > 0x01)
                {
                    value = -1;
                    return;
                }

                value |= (long)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
        }

        /*
        long bufferSizeInBytes = (long)size * sizeof(int);
        if (bufferSizeInBytes < _bulkReadThresholdInBytes)
        {
            for (int i = 0; i < size; i++)
                values[i] = reader.ReadInt32();
        }
        else
        {
            unsafe
            {
                fixed (void* dst = values)
                {
                    ReadBytes(reader, dst, bufferSizeInBytes, bufferSizeInBytes);
                }
            }
        }*/

        public static ushort[] ReadCompressedUshortArray(this Stream stream)
        {
            var count = stream.ReadCompressedInt();
            if (count == 0)
            {
                return Array.Empty<ushort>();
            }

            var values = new ushort[count];

            var index = 0;
            while (index < count)
            {
                var size = stream.ReadCompressedInt();
                if (size == 0)
                {
                    Throw(new InvalidOperationException());
                }

                if (size == sizeof(ushort))
                {
                    //TODO: shortcut to reading directly into memory.
                    //TODO: continue;
                }

                var length = stream.ReadCompressedInt();
                var end = index + length;
                for (; index < end; index++)
                {
                    stream.ReadCompressed(ref values[index]);
                }
            }

            return values;
        }

        public static int[] ReadCompressedIntArray(this Stream stream)
        {
            var count = stream.ReadCompressedInt();
            if (count == 0)
            {
                return Array.Empty<int>();
            }

            var values = new int[count];

            var index = 0;
            while (index < count)
            {
                var size = stream.ReadCompressedInt();
                if (size == 0)
                {
                    Throw(new InvalidOperationException());
                }

                if (size == sizeof(int))
                {
                    //TODO: shortcut to reading directly into memory.
                    //TODO: continue;
                }

                var length = stream.ReadCompressedInt();
                var end = index + length;
                for (; index < end; index++)
                {
                    stream.ReadCompressed(ref values[index]);
                }
            }

            return values;
        }

        public static uint[] ReadCompressedUintArray(this Stream stream)
        {
            var count = stream.ReadCompressedInt();
            if (count == 0)
            {
                return Array.Empty<uint>();
            }

            var values = new uint[count];

            var index = 0;
            while (index < count)
            {
                var size = stream.ReadCompressedInt();
                if (size == 0)
                {
                    Throw(new InvalidOperationException());
                }

                if (size == sizeof(uint))
                {
                    //TODO: shortcut to reading directly into memory.
                    //TODO: continue;
                }

                var length = stream.ReadCompressedInt();
                var end = index + length;
                for (; index < end; index++)
                {
                    stream.ReadCompressed(ref values[index]);
                }
            }

            return values;
        }

        //TODO: this method sucks
        public static long[] ReadCompressedLongArray(this Stream stream)
        {
            var count = stream.ReadCompressedInt();
            if (count == 0)
            {
                return Array.Empty<long>();
            }

            var values = AllocateUninitializedArray<long>(count);

            var index = 0;
            while (index < count)
            {
                var size = stream.ReadCompressedInt();
                if (size == 0)
                {
                    Throw(new InvalidOperationException());
                }

                if (size == sizeof(long))
                {
                    //TODO: shortcut to reading directly into memory.
                    //TODO: continue;
                }

                var length = stream.ReadCompressedInt();
                var end = index + length;
                for (; index < end; index++)
                {
                    stream.ReadCompressed(ref values[index]);
                }
            }

            return values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write<T>(this Stream stream, Span<T> values)
            where T : unmanaged
        {
            var span = MemoryMarshal.AsBytes(values);
            stream.Write(values.Length);
            stream.Write(span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [LocalsInit(false)]
        public static void Write(this Stream stream, string value)
        {
            var encoding = Utf8NoBom;
            var valueSpan = value.AsSpan();
            var len = encoding.GetByteCount(valueSpan);

            Span<byte> byteSpan = stackalloc byte[len];
            var encodedLen = encoding.GetBytes(valueSpan, byteSpan);

            stream.Write(encodedLen);
            stream.Write(byteSpan);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [LocalsInit(false)]
        public static void Write(this Stream stream, char[] value)
        {
            var encoding = Utf8NoBom;
            var valueSpan = value.AsSpan();
            var encodedLength = encoding.GetByteCount(valueSpan);

            Span<byte> byteSpan = stackalloc byte[encodedLength];
            var byteLength = encoding.GetBytes(valueSpan, byteSpan);

            stream.Write(byteLength);
            stream.Write(value.Length);
            stream.Write(byteSpan);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Read<T>(this Stream stream, ref T result)
            where T : unmanaged
        {
            var tSpan = MemoryMarshal.CreateSpan(ref result, 1);
            var span = MemoryMarshal.AsBytes(tSpan);
            stream.Read(span);
            return ref result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Read<T>(this Stream stream)
            where T : unmanaged
        {
            var result = default(T);
            var tSpan = MemoryMarshal.CreateSpan(ref result, 1);
            var span = MemoryMarshal.AsBytes(tSpan);
            stream.Read(span);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [LocalsInit(false)]
        public static char[] ReadCharArray(this Stream stream)
        {
            var byteLength = stream.Read<int>();
            var charLength = stream.Read<int>();

            Span<byte> span = stackalloc byte[byteLength];
            stream.Read(span);

            var results = AllocateUninitializedArray<char>(charLength);
            var charSpan = results.AsSpan();
            Utf8NoBom.GetChars(span, charSpan);

            return results;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [LocalsInit(false)]
        public static string ReadString(this Stream stream)
        {
            var byteLength = stream.Read<int>();
            Span<byte> bytes = stackalloc byte[byteLength];
            stream.Read(bytes);
            var result = Utf8NoBom.GetString(bytes);
            return result ?? string.Empty;
        }

        // Skips zero-initialization of the array if possible. If T contains object references,
        // the array is always zero-initialized.
        private static T[] AllocateUninitializedArray<T>(int length)
            where T : unmanaged
        {
            //TODO: Probably unnecessary because of the unmanaged constraint
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                return new T[length];
            }

            if (length == 0)
            {
                Throw(new ArgumentOutOfRangeException());
            }

            if (Unsafe.SizeOf<T>() * length < 256 - (3 * IntPtr.Size))
            {
                return new T[length];
            }

            return TypeAllocator<T>.AllocateUninitializedArray(length);
        }

        private static class TypeAllocator<T>
        {
            public static T[] AllocateUninitializedArray(int length)
            {
                if (!(MethodInfo.Invoke(null, new object[] { length }) is T[] result))
                {
                    Throw(new InvalidOperationException());
                    //For compiler to shut up
                    return Array.Empty<T>();
                }

                return result;
            }

            private static readonly MethodInfo MethodInfo = GetAllocationMethod().MakeGenericMethod(new Type[] { typeof(T) });

            //TODO: HAHAHAHAHA very hacky indeed
            private static MethodInfo GetAllocationMethod()
            {
                var t = typeof(GC);
                foreach (var mi in t.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (string.Equals(mi.Name, "AllocateUninitializedArray", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return mi;
                    }
                }

                throw new InvalidOperationException();
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] ReadArray<T>(this Stream stream)
            where T : unmanaged
        {
            if (typeof(T) == typeof(char))
            {
                Throw(new InvalidCastException("ReadArray<char>() should be replaced with a call to ReadCharArray()."));
            }

            var length = stream.Read<int>();

            var results = AllocateUninitializedArray<T>(length);
            //var results = new T[length];

            var tSpan = results.AsSpan();
            var span = MemoryMarshal.AsBytes(tSpan);
            stream.Read(span);

            return results;
        }

        //https://reubenbond.github.io/posts/dotnet-perf-tuning Section: Use static throw helpers
        //[DoesNotReturn]
        private static void Throw(Exception e)
        {
            throw e;
        }
    }
}
