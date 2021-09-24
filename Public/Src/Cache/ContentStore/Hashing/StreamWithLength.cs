// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Diagnostics.ContractsLight;
using System;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Stream that is guaranteed to have a length.
    /// </summary>
    public readonly struct StreamWithLength : IDisposable
    {
        /// <summary>
        /// MemoryStream always has a length so it can be automatically wrapped.
        /// </summary>
        public static implicit operator StreamWithLength(MemoryStream s) => s.WithLength();

        /// <summary>
        /// FileStream always has a length so it can be automatically wrapped.
        /// </summary>
        public static implicit operator StreamWithLength(FileStream s) => s.WithLength();

        /// <summary>
        /// MemoryStream always has a length so it can be automatically wrapped.
        /// </summary>
        public static implicit operator StreamWithLength?(MemoryStream? s) => s?.WithLength();

        /// <summary>
        /// FileStream always has a length so it can be automatically wrapped.
        /// </summary>
        public static implicit operator StreamWithLength?(FileStream? s) => s?.WithLength();

        /// <summary>
        /// Implicitly expose stream for all operations on it.
        /// </summary>
        public static implicit operator Stream(StreamWithLength s) => s.Stream;

        /// <summary>
        /// Underlying stream.
        /// </summary>
        public Stream Stream { get; }

        /// <summary>
        /// Length of the underlying stream.
        /// </summary>
        public long Length { get; }

        /// <summary>
        /// Called by extension methods.
        /// </summary>
        internal StreamWithLength(Stream stream, long length)
        {
            Contract.Requires(stream != null);
            Contract.Requires(length >= 0);
            Contract.Check(!stream.CanSeek || stream.Length == length)
                ?.Requires($"!stream.CanSeek || stream.Length == length fails. stream.CanSeek={stream.CanSeek}, stream.Length={stream.Length}, length={length}");
            Stream = stream;
            Length = length;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Stream.Dispose();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (Stream is FileStream fs)
            {
                return $"Length={Length}, FileStream={fs.Name}";
            }

            return $"Length={Length}, StreamType={Stream.GetType()}";
        }
    }

    /// <summary>
    /// Helpers for creating a StreamWithLength
    /// </summary>
    public static class StreamWithLengthExtensions
    {
        /// <summary>
        /// Verify at runtime that stream has a Length.
        /// </summary>
        public static StreamWithLength AssertHasLength(this Stream s)
        {
            Contract.Requires(s != null);
            Contract.Requires(s.CanSeek);
            return new StreamWithLength(s, s.Length);
        }

        /// <summary>
        /// With an explicit length.
        /// </summary>
        public static StreamWithLength WithLength(this Stream s, long length)
        {
            return new StreamWithLength(s, length);
        }

        /// <summary>
        /// Helper for safely wrapping MemoryStream.
        /// </summary>
        public static StreamWithLength WithLength(this MemoryStream s)
        {
            return new StreamWithLength(s, s.Length);
        }

        /// <summary>
        /// Helper for safely wrapping FileStream.
        /// </summary>
        public static StreamWithLength WithLength(this FileStream s)
        {
            Contract.Requires(s != null);
            return new StreamWithLength(s, s.Length);
        }
    }
}
