// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Factory methods for constructing <see cref="Result{T}"/>.
    /// </summary>
    public static class Result
    {
        /// <nodoc />
        public static Result<T> Success<T>(T result, bool isNullAllowed = false)
            => new Result<T>(result, isNullAllowed: isNullAllowed);

        /// <nodoc />
        public static Result<T> FromError<T>(ResultBase other)
        {
            Contract.Requires(!other.Succeeded);
            return new Result<T>(other);
        }

        /// <nodoc />
        public static Result<T> FromException<T>(Exception e, string? message = null) => new Result<T>(e, message);

        /// <nodoc />
        public static Result<T> FromErrorMessage<T>(string message, string? diagnostics = null) => new Result<T>(message, diagnostics);
    }

    /// <summary>
    /// Represents result of an operation with extra data.
    /// </summary>
    public class Result<T> : BoolResult, IEquatable<Result<T>>
    {
        [AllowNull]
        private readonly T _result;

        /// <summary>
        /// Creates a success outcome.
        /// </summary>
        public Result(T result) : this(result, isNullAllowed: false)
        {
        }

        /// <summary>
        /// Creates a success outcome specifying if the value can be null.
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
        public Result(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            _result = default;
        }

        /// <inheritdoc />
        public Result(Exception exception, string? message = null)
            : base(exception, message)
        {
            _result = default;
        }

        /// <inheritdoc />
        public Result(ResultBase other, string? message = null)
            : base(other, message)
        {
            _result = default;
        }

        /// <inheritdoc />
        // The following attribute allows the compiler to understand that if Succeeded is true, then Value is not null.
        // This is not technically correct, because the caller can specify 'isNullAllowed: true' in the constructor,
        // but this is a good enough behavior for us, because in vast majority of cases, isNullAllowed is false.
        [MemberNotNullWhen(true, nameof(Value))]
        // WIP ST: this is not working with the 3.7 compiler!!
        public override bool Succeeded => base.Succeeded;

        /// <summary>
        /// Gets the resulting value.
        /// </summary>
        [MaybeNull]
        public T Value
        {
            get
            {
                // Using Assert instead of Requires in order to skip message computation for successful cases.
                Contract.Check(Succeeded)?.Assert($"The operation should succeed in order to get the resulting value. Failure: {ToString()}.");
                return _result;
            }
        }

        /// <nodoc />
        public static implicit operator Result<T>(T result)
        {
            return new Result<T>(result);
        }

        /// <nodoc />
        public static implicit operator Result<T>(ErrorResult result)
        {
            return new Result<T>(result);
        }

        /// <inheritdoc />
        public bool Equals([AllowNull]Result<T> other)
        {
            return Succeeded && other?.Succeeded == true ? Value!.Equals(other.Value) : ErrorEquals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Succeeded ? Value!.GetHashCode() : base.GetHashCode();
        }
    }
}
