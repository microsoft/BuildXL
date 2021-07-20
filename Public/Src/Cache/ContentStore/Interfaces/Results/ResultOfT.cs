// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Represents result of an operation with extra data.
    /// </summary>
    public class Result<T> : BoolResult
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
        /// Creates a failed outcome.
        /// </summary>
        public Result(Error error)
            : base(error)
        {
            _result = default;
        }

        /// <summary>
        /// Creates a success outcome specifying if the value can be null.
        /// </summary>
        public Result(T result, bool isNullAllowed)
            : base(successDiagnostics: null)
        {
            if (!isNullAllowed && result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            
            _result = result;
        }

        /// <inheritdoc />
        public Result(string errorMessage, string? diagnostics = null)
            : this(Error.FromErrorMessage(errorMessage, diagnostics))
        {
        }

        /// <inheritdoc />
        public Result(Exception exception, string? message = null)
            : this(Error.FromException(exception, message))
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
        protected override bool SuccessEquals(ResultBase other)
        {
            Result<T> otherT = (Result<T>)other;

            if (Value is null)
            {
                return otherT.Value is null;
            }

            return Value.Equals(otherT.Value);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Succeeded, Succeeded ? (Value?.GetHashCode() ?? 0) : 0, base.GetHashCode()).GetHashCode();
        }

        /// <summary>
        /// Implicit conversion operator from <see cref="Result{T}"/> to <see cref="bool"/>.
        /// </summary>
        public static implicit operator bool(Result<T> result) => result.Succeeded;
    }
}
