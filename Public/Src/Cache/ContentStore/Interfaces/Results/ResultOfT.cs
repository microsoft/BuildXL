// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Factory methods for constructing <see cref="Result{T}"/>.
    /// </summary>
    public static class Result
    {
        /// <nodoc />
        public static Result<T> Success<T>(T result) => new Result<T>(result);

        /// <nodoc />
        public static Result<T> FromError<T>(ResultBase other)
        {
            Contract.Requires(!other.Succeeded);
            return new Result<T>(other);
        }

        /// <nodoc />
        public static Result<T> FromException<T>(Exception e, string message = null) => new Result<T>(e, message);

        /// <nodoc />
        public static Result<T> FromErrorMessage<T>(string message, string diagnostics = null) => new Result<T>(message, diagnostics);
    }

    /// <summary>
    /// Represents result of an operation with extra data.
    /// </summary>
    public class Result<T> : BoolResult, IEquatable<Result<T>>
    {
        private readonly T _result;

        /// <summary>
        /// Creates a success outcome.
        /// </summary>
        public Result(T result) : this(result, isNullAllowed: false)
        {
        }

        /// <summary>
        /// Creates a success outcome specifing if the value can be null.
        /// </summary>
        public Result(T result, bool isNullAllowed)
        {
            if (!isNullAllowed && result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            
            _result = result;
        }

        /// <inheritdoc />
        public Result(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <inheritdoc />
        public Result(Exception exception, string message = null)
            : base(exception, message)
        {
        }

        /// <inheritdoc />
        public Result(ResultBase other, string message = null)
            : base(other, message)
        {
        }

        /// <summary>
        /// Gets the counters.
        /// </summary>
        public T Value
        {
            get
            {
                // Using Assert instead of Requires in order to skip message computation for successful cases.
                if (!Succeeded)
                {
                    Contract.Assert(false, $"The operation should succeed in order to get the resulting value. Failure: {ToString()}.");
                }

                return _result;
            }
        }

        /// <nodoc />
        public static implicit operator Result<T>(T result)
        {
            return new Result<T>(result);
        }

        /// <inheritdoc />
        public bool Equals(Result<T> other)
        {
            return Succeeded && other.Succeeded ? Value.Equals(other.Value) : ErrorEquals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Succeeded ? Value.GetHashCode() : base.GetHashCode();
        }
    }
}
