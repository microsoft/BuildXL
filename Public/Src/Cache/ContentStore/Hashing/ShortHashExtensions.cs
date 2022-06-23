// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <nodoc />
    public static class ShortHashExtensions
    {
#if NET_COREAPP
        /// <summary>
        /// Gets a byte representation of <paramref name="hash"/>.
        /// </summary>
        /// <remarks>
        /// The "Unsafe" part comes from the lifetime issues possible with this method.
        /// If the result of this method outlives the argument, then the behavior is undefined:
        /// <code>ReadOnlySpan&lt;byte&gt; TotallyUnsafe() => default(ShortHash).AsSpanUnsafe();</code>
        /// The 'TotallyUnsafe' method will return a byte representation of the old stack frame.
        ///
        /// But besides the lifetime issue, the implementation is safe.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<byte> AsSpanUnsafe(this in ShortHash hash) => MemoryHelpers.AsBytesUnsafe(hash);
#else
        /// <summary>
        /// Gets the byte array representation of <paramref name="hash"/> and then gets the span representation out of ot.
        /// </summary>
        /// <remarks>
        /// This implementation is not efficient, because the Full Framework doesn't have a runtime support for <see cref="Span{T}"/>
        /// that is needed to make the implementation GC safe.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<byte> AsSpanUnsafe(this in ShortHash hash) => hash.ToByteArray().AsSpan();
#endif

        /// <nodoc />
        public static void Write(this BinaryWriter writer, in ShortHash value)
        {
            value.Serialize(writer);
        }

        /// <nodoc />
        public static unsafe ShortHash ReadShortHash(this BinaryReader reader)
        {
#if NETCOREAPP
            Span<ShortHash> result = stackalloc ShortHash[1];
            // Ignoring the result, because we might read fewer bytes than requested.
            _ = reader.Read(MemoryMarshal.AsBytes(result));

            return result[0];
#else
            var length = ShortHash.SerializedLength;
            using var pooledHandle = ContentHashExtensions.ShortHashBytesArrayPool.Get();

            var bytesRead = reader.Read(pooledHandle.Value, index: 0, count: length);

            return ShortHash.FromSpan(pooledHandle.Value.AsSpan(0, length: bytesRead));
#endif
        }

        /// <nodoc />
        public static ShortHash ToShortHash(this ContentHash contentHash) => new ShortHash(contentHash);
    }
}
