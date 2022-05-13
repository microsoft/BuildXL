// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;

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
    }
}
