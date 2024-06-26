// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#nullable enable

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// Polyfill for <see cref="MemoryMarshal"/> on .NET Standard 2.0 / .NETFX 4.7.2
    /// </summary>
    public static class MemoryMarshalShim
    {
#if NETCOREAPP
        /// <summary>
        /// <see cref="MemoryMarshal.CreateSpan{T}(ref T, int)"/>
        /// </summary>
#else
        /// <summary>
        /// <see cref="MemoryMarshal"/>.CreateSpan{T}(ref T, int)
        /// </summary>
#endif
        public static unsafe Span<T> CreateSpan<T>(ref T value, int length)
        {
#if NETCOREAPP
            return MemoryMarshal.CreateSpan(ref value, length);
#else
            return new Span<T>(Unsafe.AsPointer(ref value), length);
#endif
        }

        /// <summary>
        /// Returns span as possible string format argument. On .NET Core this just returns span. On .NET FX it returns the string.
        /// </summary>
#if NETCOREAPP
        public static ReadOnlySpan<T> AsFormattable<T>(this ReadOnlySpan<T> s) => s;
#else
        public static string AsFormattable<T>(this ReadOnlySpan<T> s) => s.ToString();
#endif
    }
}
