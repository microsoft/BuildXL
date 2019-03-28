// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <nodoc />
    public static class ObjectResult
    {
        /// <nodoc />
        public static ObjectResult<T> Create<T>(T data) where T : class
            => new ObjectResult<T>(data);
    }

    /// <summary>
    ///     A boolean operation that returns an object.
    /// </summary>
    public class ObjectResult<T> : BoolResult, IEquatable<ObjectResult<T>>
        where T : class
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult()
            : base(false)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult(T obj)
            : base(true)
        {
            Contract.Requires(obj != null);
            Data = obj;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult(Exception exception, string message = null)
            : base(exception, message)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectResult{T}"/> class.
        /// </summary>
        public ObjectResult(ResultBase other, string message = null)
            : base(other, message)
        {
        }

        /// <summary>
        /// Gets the result data.
        /// </summary>
        /// <remarks>
        /// If operation succeeded the returning value is not null.
        /// </remarks>
        public T Data { get; }

        /// <inheritdoc />
        public bool Equals(ObjectResult<T> other)
        {
            if (!base.Equals(other) || other == null)
            {
                return false;
            }

            if (ReferenceEquals(Data, other.Data))
            {
                return true;
            }

            if (Data == null || other.Data == null)
            {
                return false;
            }

            return Data.Equals(other.Data);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            var other = obj as ObjectResult<T>;
            return Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ (Data?.GetHashCode() ?? 0);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded ? $"Success Data=[{Data}]" : GetErrorString();
        }
    }
}
