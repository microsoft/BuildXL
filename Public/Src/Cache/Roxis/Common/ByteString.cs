// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace BuildXL.Cache.Roxis.Common
{
    /// <summary>
    /// Convenience class to wrap around our usages of strings, byte arrays, etc.
    /// </summary>
    public struct ByteString : IEquatable<ByteString>
    {
        public byte[] Value { get; }

        public ByteString(byte[] value)
        {
            Value = value;
        }

        // TODO: Remove all implicit conversions and make sure all callers are doing explicit conversions.
        public static implicit operator ByteString?(string? value)
        {
            return value == null ? (ByteString?)null : new ByteString(Encoding.UTF8.GetBytes(value));
        }

        public static implicit operator ByteString(string value)
        {
            return new ByteString(Encoding.UTF8.GetBytes(value));
        }

        public static implicit operator ByteString(long value)
        {
            return BitConverter.GetBytes(value);
        }

        public static implicit operator ByteString?(byte[]? value)
        {
            return value == null ? (ByteString?)null : new ByteString(value);
        }

        public static implicit operator ByteString(byte[] value)
        {
            return new ByteString(value);
        }

        public static implicit operator byte[](ByteString byteString)
        {
            return byteString.Value;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
            {
                return false;
            }

            return obj is ByteString byteString && Equals(byteString);
        }

        public bool Equals([AllowNull]ByteString other)
        {
            return Value.SequenceEqual(other.Value);
        }

        public override int GetHashCode()
        {
            return -1939223833 + EqualityComparer<byte[]>.Default.GetHashCode(Value);
        }

        public static bool operator ==(ByteString left, ByteString right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ByteString left, ByteString right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            if (Value.Any(b => b > 128))
            {
                return BitConverter.ToString(Value);
            }

            return Encoding.UTF8.GetString(Value);
        }
    }
}
