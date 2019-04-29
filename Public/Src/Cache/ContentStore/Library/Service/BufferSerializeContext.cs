// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     Context for tracking read/write movement through a byte buffer.
    /// </summary>
    public class BufferSerializeContext
    {
        private readonly byte[] _buffer;
        private int _offset;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BufferSerializeContext"/> class.
        /// </summary>
        public BufferSerializeContext(byte[] buffer)
        {
            Contract.Requires(buffer != null);

            _buffer = buffer;
            _offset = 0;
        }

        /// <summary>
        ///     Gets current offset.
        /// </summary>
        public int Offset => _offset;

        /// <summary>
        ///     Gets the buffer length;
        /// </summary>
        public int Length => _buffer.Length;

        /// <summary>
        ///     Check if there is more data, of the specified size, beyond the current offset.
        /// </summary>
        public bool HasMoreData(int size)
        {
            return (_offset + size) <= _buffer.Length;
        }

        /// <summary>
        ///     Serialize an 8-bit integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Serialize(byte value)
        {
            _buffer[_offset++] = value;
        }

        /// <summary>
        ///     Deserialize a 8-bit integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte DeserializeByte()
        {
            return _buffer[_offset++];
        }

        /// <summary>
        ///     Serialize a 32-bit integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Serialize(int value)
        {
            unchecked {
                _buffer[_offset++] = (byte)value;
                _buffer[_offset++] = (byte)(value >> 8);
                _buffer[_offset++] = (byte)(value >> 16);
                _buffer[_offset++] = (byte)(value >> 24);
            }
        }

        /// <summary>
        ///     Serialize a 32-bit integer starting at a specific offset.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Serialize(int value, int offset)
        {
            unchecked {
                _buffer[offset++] = (byte)value;
                _buffer[offset++] = (byte)(value >> 8);
                _buffer[offset++] = (byte)(value >> 16);
                _buffer[offset] = (byte)(value >> 24);
            }
        }

        /// <summary>
        ///     Deserialize a 32-bit integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int DeserializeInt32()
        {
            return _buffer[_offset++] | (_buffer[_offset++] << 8) | (_buffer[_offset++] << 16) | (_buffer[_offset++] << 24);
        }

        /// <summary>
        ///     Serialize a 64-bit integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Serialize(long value)
        {
            unchecked {
                _buffer[_offset++] = (byte)value;
                _buffer[_offset++] = (byte)(value >> 8);
                _buffer[_offset++] = (byte)(value >> 16);
                _buffer[_offset++] = (byte)(value >> 24);
                _buffer[_offset++] = (byte)(value >> 32);
                _buffer[_offset++] = (byte)(value >> 40);
                _buffer[_offset++] = (byte)(value >> 48);
                _buffer[_offset++] = (byte)(value >> 56);
            }
        }

        /// <summary>
        ///     Deserialize a 64-bit integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long DeserializeInt64()
        {
            return
                _buffer[_offset++] |
                (long)_buffer[_offset++] << 8 |
                (long)_buffer[_offset++] << 16 |
                (long)_buffer[_offset++] << 24 |
                (long)_buffer[_offset++] << 32 |
                (long)_buffer[_offset++] << 40 |
                (long)_buffer[_offset++] << 48 |
                (long)_buffer[_offset++] << 56;
        }

        /// <summary>
        ///     Serialize a string.
        /// </summary>
        public void Serialize(string value)
        {
            Contract.Requires(value != null);

            if (value.Length == 0)
            {
                _buffer[_offset++] = 0;
                _buffer[_offset++] = 0;
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);

            if (bytes.Length > ushort.MaxValue)
            {
                throw new ArgumentException("UTF-8 string size cannot be > uint16.max");
            }

            unchecked {
                _buffer[_offset++] = (byte)(bytes.Length >> 8);
                _buffer[_offset++] = (byte)bytes.Length;
            }

            foreach (var b in bytes)
            {
                _buffer[_offset++] = b;
            }
        }

        /// <summary>
        ///     Deserialize a string from a buffer.
        /// </summary>
        public string DeserializeString()
        {
            int len = (_buffer[_offset++] << 8) | _buffer[_offset++];
            string s = Encoding.UTF8.GetString(_buffer, _offset, len);
            _offset += len;
            return s;
        }

        /// <summary>
        ///     Serialize a GUID.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Serialize(Guid id)
        {
            var bytes = id.ToByteArray();
            for (var i = 0; i < 16; i++)
            {
                _buffer[_offset++] = bytes[i];
            }
        }

        /// <summary>
        ///     Deserialize a GUID.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Guid DeserializeGuid()
        {
            unchecked {
                int a = _buffer[_offset++] | (_buffer[_offset++] << 8) | (_buffer[_offset++] << 16) | (_buffer[_offset++] << 24);
                short b = (short)(_buffer[_offset++] | (_buffer[_offset++] << 8));
                short c = (short)(_buffer[_offset++] | (_buffer[_offset++] << 8));

                return new Guid(
                    a,
                    b,
                    c,
                    _buffer[_offset++],
                    _buffer[_offset++],
                    _buffer[_offset++],
                    _buffer[_offset++],
                    _buffer[_offset++],
                    _buffer[_offset++],
                    _buffer[_offset++],
                    _buffer[_offset++]
                );
            }
        }

        /// <summary>
        ///     Serialize a trimmed ContentHash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Serialize(ContentHash contentHash)
        {
            contentHash.Serialize(_buffer, _offset);
            _offset += contentHash.ByteLength + 1;
        }

        /// <summary>
        ///     Serialize a full ContentHash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SerializeFull(ContentHash contentHash)
        {
            contentHash.Serialize(_buffer, _offset, ContentHash.SerializeHashBytesMethod.Full);
            _offset += ContentHash.SerializedLength;
        }

        /// <summary>
        ///     Deserialize a trimmed ContentHash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ContentHash DeserializeContentHash()
        {
            var contentHash = new ContentHash(_buffer, _offset);
            _offset += contentHash.ByteLength + 1;
            return contentHash;
        }

        /// <summary>
        ///     Deserialize a full ContentHash.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ContentHash DeserializeFullContentHash()
        {
            var contentHash = new ContentHash(_buffer, _offset, ContentHash.SerializeHashBytesMethod.Full);
            _offset += ContentHash.SerializedLength;
            return contentHash;
        }

        /// <summary>
        ///     Serialize a path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Serialize(PathBase path)
        {
            Serialize(path.Path);
        }

        /// <summary>
        ///     Deserialize an AbsolutePath.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AbsolutePath DeserializeAbsolutePath()
        {
            return new AbsolutePath(DeserializeString());
        }

        /// <summary>
        ///     Serialize a buffer to a buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Serialize(byte[] value)
        {
            foreach (var b in value)
            {
                _buffer[_offset++] = b;
            }
        }

        /// <summary>
        ///     Deserialize bytes.
        /// </summary>
        public void DeserializeBytes(int length, out byte[] buffer, out int offset)
        {
            Contract.Requires((Offset + length) <= Length);

            buffer = _buffer;
            offset = _offset;
            _offset += length;
        }
    }
}
