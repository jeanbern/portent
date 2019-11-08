using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using LocalsInit;

// ReSharper disable ForCanBeConvertedToForeach

namespace portent
{
    public static class StreamExtensions
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, true);

        #region Read

        #region Signed

        private static long ReadCompressedSigned(this Stream stream)
        {
            var shift = 0;
            var value = 0UL;
            var b = (byte)stream.ReadByte();
            while ((b & 0x80) != 0)
            {
                value |= (ulong)(b & 0x7F) << shift;
                shift += 7;
                b = (byte)stream.ReadByte();
            }

            value |= (ulong)(b & 0x3f) << shift;

            if ((b & 0x40) == 0)
            {
                return (long) value;
            }

            if (value != 0)
            {
                return -(long) value;
            }

            return long.MinValue;
        }

        public static int ReadCompressedInt(this Stream stream)
        {
            return (int) stream.ReadCompressedSigned();
        }

        public static unsafe void ReadCompressed(this Stream stream, int* pointer, uint count)
        {
            while (count > 0)
            {
                *pointer = stream.ReadCompressedInt();
                pointer++;
                count--;
            }
        }

        #endregion Signed

        #region Unsigned

        private static ulong ReadCompressedUnsigned(this Stream stream)
        {
            var shift = 0;
            var value = 0UL;
            int read;
            do
            {
                read = stream.ReadByte();
                if (read == -1)
                {
                    Throw(new EndOfStreamException());
                }

                value |= (ulong)(read & 0x7F) << shift;
                shift += 7;
            } while (read >= 0x80);

            return value;
        }

        public static ushort ReadCompressedUShort(this Stream stream)
        {
            var b = stream.ReadByte();
            if (b == -1)
            {
                Throw(new EndOfStreamException());
            }

            if (b < 0x80)
            {
                return (ushort) b;
            }

            var b2 = stream.ReadByte();
            if (b2 == -1)
            {
                Throw(new EndOfStreamException());
            }

            var result = (b2 << 7) | (b & 0x3f);
            if (result < 0x8000)
            {
                return (ushort) result;
            }

            var b3 = stream.ReadByte();
            if (b3 == -1)
            {
                Throw(new EndOfStreamException());
            }

            return (ushort) ((b3 << 14) | (result & 0x3fff));
        }

        public static uint ReadCompressedUInt(this Stream stream)
        {
            return (uint) ReadCompressedULong(stream);
        }

        public static ulong ReadCompressedULong(this Stream stream)
        {
            return ReadCompressedUnsigned(stream);
        }

        public static unsafe void ReadCompressed(this Stream stream, ushort* pointer, uint count)
        {
            while (count > 0)
            {
                *pointer = stream.ReadCompressedUShort();
                pointer++;
                count--;
            }
        }

        public static unsafe void ReadCompressed(this Stream stream, ulong* pointer, uint count)
        {
            while (count > 0)
            {
                *pointer = stream.ReadCompressedULong();
                pointer++;
                count--;
            }
        }

        public static unsafe void ReadSequentialCompressedUshortToUint(this Stream stream, uint* pointer, uint count)
        {
            var currentValue = 0u;
            while (count > 0)
            {
                var stepValue = stream.ReadCompressedUShort();
                currentValue += stepValue;
                *pointer = currentValue;
                pointer++;
                count--;
            }
        }

        #endregion Unsigned

        public static unsafe void ReadUtf8(this Stream stream, char* pointer, uint byteLength, uint charLength)
        {
            Span<byte> span = stackalloc byte[(int) byteLength];
            stream.Read(span);
            var spp = new Span<char>(pointer, (int) charLength);
            Utf8NoBom.GetChars(span, spp);
        }

        #endregion Read

        #region Write

        #region Signed

        public static void WriteCompressed(this Stream stream, int value)
        {
            WriteCompressed(stream, (long) value);
        }

        private static void WriteCompressed(this Stream stream, long value)
        {
            var negative = false;
            if (value < 0)
            {
                negative = true;
                value = -value;
            }

            while (value >= 0x40)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            if (negative)
            {
                value |= 0x40;
            }

            stream.WriteByte((byte)value);
        }

        public static void WriteCompressed(this Stream stream, Span<int> values)
        {
            stream.WriteCompressed((uint) values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                stream.WriteCompressed(values[i]);
            }
        }

        #endregion Signed

        #region Unsigned

        public static void WriteCompressed(this Stream stream, ushort value)
        {
            stream.WriteCompressed((ulong)value);
        }

        public static void WriteCompressed(this Stream stream, uint value)
        {
            stream.WriteCompressed((ulong)value);
        }

        public static void WriteCompressed(this Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                var byteToWrite = (byte) ((value & 0x7f) | 0x80);
                stream.WriteByte(byteToWrite);
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        public static void WriteCompressed(this Stream stream, Span<ushort> values)
        {
            stream.WriteCompressed((uint) values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                stream.WriteCompressed(values[i]);
            }
        }

        public static void WriteCompressed(this Stream stream, Span<ulong> values)
        {
            stream.WriteCompressed((uint) values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                stream.WriteCompressed(values[i]);
            }
        }

        public static void WriteSequentialCompressedToUshort(this Stream stream, Span<uint> values)
        {
            stream.WriteCompressed((uint) values.Length);
            var previous = 0u;

            for (var i = 0; i < values.Length; i++)
            {
                var current = values[i];
                var difference = current - previous;
                previous = current;
                stream.WriteCompressed((ushort) difference);
            }
        }

        #endregion Unsigned

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [LocalsInit(false)]
        public static void WriteUtf8(this Stream stream, char[] value)
        {
            var encoding = Utf8NoBom;
            var valueSpan = value.AsSpan();
            var encodedLength = encoding.GetByteCount(valueSpan);

            Span<byte> byteSpan = stackalloc byte[encodedLength];
            var byteLength = encoding.GetBytes(valueSpan, byteSpan);

            stream.WriteCompressed((uint) byteLength);
            stream.WriteCompressed((uint) value.Length);
            stream.Write(byteSpan);
        }

        #endregion Write

        //https://reubenbond.github.io/posts/dotnet-perf-tuning Section: Use static throw helpers
        [Conditional("DEBUG")]
        private static void Throw(Exception e)
        {
            throw e;
        }
    }
}
