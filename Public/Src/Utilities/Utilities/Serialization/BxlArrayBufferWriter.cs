// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;

// The code is adopted from here: https://github.com/dotnet/runtime/blob/57bfe474518ab5b7cfe6bf7424a79ce3af9d6657/src/libraries/Common/src/System/Buffers/ArrayBufferWriter.cs
// with some minor changes
namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// Represents a heap-based, array-backed output sink into which <typeparam name="T"/> data can be written.
    /// </summary>
    public sealed class BxlArrayBufferWriter<T> : IBufferWriter<T>
    {
        // Copy of Array.MaxLength.
        // Used by projects targeting .NET Framework.
        private const int ArrayMaxLength = 0x7FFFFFC7;

        private const int DefaultInitialBufferSize = 256;

        private T[] m_buffer;
        private int m_index;

        /// <summary>
        /// Creates an instance of an <see cref="ArrayBufferWriter{T}"/>, in which data can be written to,
        /// with the default initial capacity.
        /// </summary>
        public BxlArrayBufferWriter()
        {
            m_buffer = Array.Empty<T>();
            m_index = 0;
        }

        /// <summary>
        /// Creates an instance of an <see cref="ArrayBufferWriter{T}"/>, in which data can be written to,
        /// with an initial capacity specified.
        /// </summary>
        /// <param name="initialCapacity">The minimum capacity with which to initialize the underlying buffer.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="initialCapacity"/> is not positive (i.e. less than or equal to 0).
        /// </exception>
        public BxlArrayBufferWriter(int initialCapacity)
        {
            Contract.Requires(initialCapacity >= 0);
            
            m_buffer = new T[initialCapacity];
            m_index = 0;
        }

        /// <summary>
        /// Returns the data written to the underlying buffer so far, as a <see cref="ReadOnlyMemory{T}"/>.
        /// </summary>
        public ReadOnlyMemory<T> WrittenMemory => m_buffer.AsMemory(0, m_index);

        /// <summary>
        /// Returns the data written to the underlying buffer so far, as a <see cref="ReadOnlySpan{T}"/>.
        /// </summary>
        public ReadOnlySpan<T> WrittenSpan => m_buffer.AsSpan(0, m_index);

        /// <summary>
        /// Returns the amount of data written to the underlying buffer so far.
        /// </summary>
        public int WrittenCount => m_index;

        /// <summary>
        /// Returns the total amount of space within the underlying buffer.
        /// </summary>
        public int Capacity => m_buffer.Length;

        /// <summary>
        /// Returns the amount of space available that can still be written into without forcing the underlying buffer to grow.
        /// </summary>
        public int FreeCapacity => m_buffer.Length - m_index;

        /// <summary>
        /// Clears the data written to the underlying buffer.
        /// </summary>
        /// <remarks>
        /// You must clear the <see cref="ArrayBufferWriter{T}"/> before trying to re-use it.
        /// </remarks>
        public void Clear()
        {
            Debug.Assert(m_buffer.Length >= m_index);
            
            m_buffer.AsSpan(0, m_index).Clear();

            m_index = 0;
        }

        /// <summary>
        /// Sets the position for the buffer.
        /// </summary>
        public void SetPosition(int position)
        {
            Contract.Requires(position < m_buffer.Length);
            m_index = position;
        }

        /// <summary>
        /// Notifies <see cref="IBufferWriter{T}"/> that <paramref name="count"/> amount of data was written to the output <see cref="Span{T}"/>/<see cref="Memory{T}"/>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="count"/> is negative.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when attempting to advance past the end of the underlying buffer.
        /// </exception>
        /// <remarks>
        /// You must request a new buffer after calling Advance to continue writing more data and cannot write to a previously acquired buffer.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining
#if NET60_OR_GREATER
                    | MethodImplOptions.AggressiveOptimization
#endif
                    )] // This method is called a lot.
        public void Advance(int count)
        {
            Contract.Requires(count >= 0);

            if (m_index > m_buffer.Length - count)
                ThrowInvalidOperationException_AdvancedTooFar(m_buffer.Length);

            m_index += count;
        }

        /// <summary>
        /// Returns a <see cref="Memory{T}"/> to write to that is at least the requested length (specified by <paramref name="sizeHint"/>).
        /// If no <paramref name="sizeHint"/> is provided (or it's equal to <code>0</code>), some non-empty buffer is returned.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="sizeHint"/> is negative.
        /// </exception>
        /// <remarks>
        /// This will never return an empty <see cref="Memory{T}"/>.
        /// </remarks>
        /// <remarks>
        /// There is no guarantee that successive calls will return the same buffer or the same-sized buffer.
        /// </remarks>
        /// <remarks>
        /// You must request a new buffer after calling Advance to continue writing more data and cannot write to a previously acquired buffer.
        /// </remarks>
        public Memory<T> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            Debug.Assert(m_buffer.Length > m_index);
            return m_buffer.AsMemory(m_index);
        }

        /// <inheritdoc cref="GetSpan(int)"/>
        public Span<T> GetSpan(int sizeHint, bool fromStart)
        {
            CheckAndResizeBuffer(sizeHint);
            Debug.Assert(m_buffer.Length > m_index);

            int index = fromStart ? 0 : m_index;
            return m_buffer.AsSpan(index);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> to write to that is at least the requested length (specified by <paramref name="sizeHint"/>).
        /// If no <paramref name="sizeHint"/> is provided (or it's equal to <code>0</code>), some non-empty buffer is returned.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="sizeHint"/> is negative.
        /// </exception>
        /// <remarks>
        /// This will never return an empty <see cref="Span{T}"/>.
        /// </remarks>
        /// <remarks>
        /// There is no guarantee that successive calls will return the same buffer or the same-sized buffer.
        /// </remarks>
        /// <remarks>
        /// You must request a new buffer after calling Advance to continue writing more data and cannot write to a previously acquired buffer.
        /// </remarks>
        public Span<T> GetSpan(int sizeHint = 0) => GetSpan(sizeHint, fromStart: false);

        private void CheckAndResizeBuffer(int sizeHint)
        {
            Contract.Requires(sizeHint >= 0);

            if (sizeHint == 0)
            {
                sizeHint = 1;
            }

            if (sizeHint > FreeCapacity)
            {
                int currentLength = m_buffer.Length;

                // Attempt to grow by the larger of the sizeHint and double the current size.
                int growBy = Math.Max(sizeHint, currentLength);

                if (currentLength == 0)
                {
                    growBy = Math.Max(growBy, DefaultInitialBufferSize);
                }

                int newSize = currentLength + growBy;

                if ((uint)newSize > int.MaxValue)
                {
                    // Attempt to grow to ArrayMaxLength.
                    uint needed = (uint)(currentLength - FreeCapacity + sizeHint);
                    Debug.Assert(needed > currentLength);

                    if (needed > ArrayMaxLength)
                    {
                        ThrowOutOfMemoryException(needed);
                    }

                    newSize = ArrayMaxLength;
                }

                Array.Resize(ref m_buffer, newSize);
            }

            Debug.Assert(FreeCapacity > 0 && FreeCapacity >= sizeHint);
        }

        private static void ThrowInvalidOperationException_AdvancedTooFar(int capacity)
        {
            throw new InvalidOperationException($"Cannot advance past the end of the buffer, which has a size of {capacity}.");
        }

        private static void ThrowOutOfMemoryException(uint capacity)
        {
            throw new OutOfMemoryException($"Cannot allocate a buffer of size {capacity}.");
        }
    }
}