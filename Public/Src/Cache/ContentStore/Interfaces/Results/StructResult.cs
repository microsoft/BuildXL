// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <nodoc />
    public static class StructResult
    {
        /// <nodoc />
        public static StructResult<T> Create<T>(T data) where T: struct
            => new StructResult<T>(data);
    }

    /// <summary>
    ///     A boolean operation that returns a struct on success.
    /// </summary>
    /// <remarks>
    /// Wrapper for value types over object types and created separately.
    /// this is because IEquatable doesn't support struct/value types.
    /// </remarks>
    public class StructResult<T> : BoolResult
        where T : struct
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult()
            : base(false)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult(T obj)
            : base (true)
        {
            Data = obj;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult(Exception exception, string message = null)
            : base(exception, message)
        {
            Contract.Requires(exception != null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult(ResultBase other, string message = null)
            : base(other, message)
        {
            Contract.Requires(other != null);
        }

        /// <summary>
        ///     Gets the result data.
        /// </summary>
        public T Data { get; }

        /// <nodoc />
        public static implicit operator StructResult<T>(T data)
        {
            return new StructResult<T>(data);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ Data.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded ? $"Success Data=[{Data}]" : GetErrorString();
        }
    }
}
