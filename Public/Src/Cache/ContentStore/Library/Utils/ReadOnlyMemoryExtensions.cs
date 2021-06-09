// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BuildXL.Cache.ContentStore.Utils
{
    // TODO: this class should be moved to utilities eventually, but right now we don't have an utilities project that references System.Memory
    /// <summary>
    /// Extension methods for <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    public static class ReadOnlyMemoryExtensions
    {
        /// <summary>
        /// Creates an instance of <see cref="MemoryStream"/> and the flag that indicates whether a new array was allocated.
        /// </summary>
        /// <remarks>
        /// The method tries to reuse an array the <paramref name="input"/> uses, but in some cases (theoretically)
        /// the underlying array maybe unavailable and the new array will be created.
        /// Btw, I haven't find a way to force a new array allocation here, but it is good to have a way to check it by having a flag in the result.
        /// </remarks>
        public static MemoryStream AsMemoryStream(this ReadOnlyMemory<byte> input, out bool newArrayWasCreated)
        {
            if (MemoryMarshal.TryGetArray(input, out var segment))
            {
                newArrayWasCreated = false;
                return 
                    new MemoryStream(segment.Array!, index: segment.Offset, count: segment.Count, writable: false);
            }

            newArrayWasCreated = true;
            return new MemoryStream(input.ToArray());
        }

        /// <inheritdoc cref="AsMemoryStream(System.ReadOnlyMemory{byte},out bool)"/>
        public static MemoryStream AsMemoryStream(this ReadOnlyMemory<byte> input) => input.AsMemoryStream(out _);

        /// <summary>
        /// Creates a ReadOnlyMemory for the entire span of the memory stream
        /// </summary>
        public static ReadOnlyMemory<byte> AsReadOnlyMemory(this MemoryStream stream)
        {
            return stream.GetBuffer().AsMemory(0, (int)stream.Length);
        }
    }
}
