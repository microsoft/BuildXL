// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

#nullable enable

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// An exception thrown by <see cref="SpanWriter.EnsureLength"/> or <see cref="SpanReader.EnsureLength"/> methods
    /// if the underlying span is too small.
    /// </summary>
    public sealed class InsufficientLengthException : InvalidOperationException
    {
        /// <nodoc />
        public InsufficientLengthException(int minLength, int remainingLength)
        {
            MinLength = minLength;
            RemainingLength = remainingLength;
        }

        /// <summary>
        /// Throw <see cref="InsufficientLengthException"/> to allow the caller to be inlined.
        /// </summary>
        public static void Throw(int minLength, int remainingLength) => throw new InsufficientLengthException(minLength, remainingLength);

        /// <nodoc />
        public int MinLength { get; }

        /// <nodoc />
        public int RemainingLength { get; }

        /// <inheritdoc />
        public override string Message => $"The buffer should have at least {MinLength} length but has {RemainingLength}.";
    }
}