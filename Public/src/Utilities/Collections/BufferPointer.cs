// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Pointer into an array for use in structures that encapsulate multiple arrays and need a consistent
    /// way of passing out pointers to those array.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct BufferPointer<T>
    {
        /// <summary>
        /// The index in the buffer
        /// </summary>
        public readonly int Index;

        /// <summary>
        /// The buffer
        /// </summary>
        [SuppressMessage("Microsoft.Security", "CA2105:ArrayFieldsShouldNotBeReadOnly")]
        public readonly T[] Buffer;

        /// <summary>
        /// Constructor
        /// </summary>
        public BufferPointer(T[] buffer, int index)
        {
            Buffer = buffer;
            Index = index;
        }

        /// <summary>
        /// Gets or sets the entry for the index in the buffer
        /// </summary>
        public T Value
        {
            get => Buffer[Index];

            set => Buffer[Index] = value;
        }
    }

    /*
    /// <summary>
    /// Extension methods for buffer pointers
    /// </summary>
    public static class BufferPointer
    {
        /// <summary>
        /// Performs an interlocked compare exchange on the slot specified by the buffer pointer
        /// </summary>
        /// <param name="bufferPointer">the buffer pointer indicating the slot</param>
        /// <param name="value">The value that replaces the destination value if the comparison results in equality.</param>
        /// <param name="comparand">The value that is compared to the value at the buffer pointer slot.</param>
        /// <returns>The original value at the buffer pointer slot</returns>
        public static int CompareExchange(this BufferPointer<int> bufferPointer, int value, int comparand)
        {
            return Interlocked.CompareExchange(ref bufferPointer.Buffer[bufferPointer.Index], value, comparand);
        }

        /// <summary>
        /// Performs an interlocked exchange on the slot specified by the buffer pointer
        /// </summary>
        /// <param name="bufferPointer">the buffer pointer indicating the slot</param>
        /// <param name="value">The value that replaces the destination value.</param>
        /// <returns>The original value at the buffer pointer slot</returns>
        public static int Exchange(this BufferPointer<int> bufferPointer, int value)
        {
            return Interlocked.Exchange(ref bufferPointer.Buffer[bufferPointer.Index], value);
        }
    }
    */
}
