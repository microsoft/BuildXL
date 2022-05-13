// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// A set of helper methods for dealing with low level span interpretation.
    /// </summary>
    public static class MemoryHelpers
    {
#if NET_COREAPP
        /// <summary>
        /// Interprets a struct instance <paramref name="value"/> as an array of bytes.
        /// </summary>
        /// <remarks>
        /// The method is memory safe only if the lifetime of the resulting span is shorter then the lifetime of the <paramref name="value"/>.
        /// I.e. the following code is not safe: <code>ReadOnlySpan&lt;byte&gt; Unsafe() => AsBytesUnsafe(42); </code>.
        ///
        /// But besides the lifetime issue, this method is memory safe because it relies on the runtime support for <see cref="Span{T}"/> and
        /// <see cref="ReadOnlySpan{T}"/> that allows the GC to track the lifetime of <paramref name="value"/> even if it's an
        /// interior pointer of a managed object.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static ReadOnlySpan<byte> AsBytesUnsafe<T>(in T value)
            where T : unmanaged
        {
            return MemoryMarshal.AsBytes(
                MemoryMarshal.CreateReadOnlySpan(
                    reference: ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value)),
                    length: Unsafe.SizeOf<T>()));
        }
#endif
    }
}
