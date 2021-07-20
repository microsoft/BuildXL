// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Represents the result of some possibly-successful function.
    /// </summary>
    public partial class Result : ResultBase
    {
        /// <nodoc />
        protected Result(string? successDiagnostics)
            : base(successDiagnostics)
        {
        }

        /// <nodoc />
        protected Result(Error error)
            : base(error)
        {
        }

        /// <summary>
        /// Creates a new instance of a failed result.
        /// </summary>
        public Result(string errorMessage, string? diagnostics = null)
            : base(Error.FromErrorMessage(errorMessage, diagnostics))
        {
            Contract.RequiresNotNullOrEmpty(errorMessage);
        }

        /// <summary>
        /// Creates a new instance of a failed result.
        /// </summary>
        public Result(Exception exception, string? message = null)
            : base(exception, message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Result" /> class.
        /// </summary>
        public Result(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        /// <summary>
        /// Overloads &amp; operator to behave as AND operator.
        /// </summary>
        public static Result operator &(Result result1, Result result2)
        {
            if (result1.Succeeded)
            {
                return result2;
            }

            if (result2.Succeeded)
            {
                return result1;
            }

            // We merge the errors the same way for '|' and '&' operators.
            return MergeFailures(result1, result2);
        }

        /// <summary>
        /// Overloads | operator to behave as OR operator.
        /// </summary>
        public static Result operator |(Result? left, Result? right)
        {
            // One of the arguments may be null but not both.

            if (left == null)
            {
                Contract.AssertNotNull(right);
                return right;
            }

            if (right == null)
            {
                Contract.AssertNotNull(left);
                return left;
            }

            if (left.Succeeded)
            {
                return left;
            }

            if (right.Succeeded)
            {
                return right;
            }

            // We merge the errors the same way for '|' and '&' operators.
            return MergeFailures(left, right);
        }

        private static Result MergeFailures(Result left, Result right)
            => MergeFailures(left, right, () => new Result(successDiagnostics: null), error => new Result(error));

        /// <summary>
        /// Implicit conversion operator from <see cref="Result"/> to <see cref="bool"/>.
        /// </summary>
        public static implicit operator bool(Result result) => result.Succeeded;
    }
}
