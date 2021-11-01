// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Operation result the boolean operation.
    /// </summary>
    /// <remarks>
    /// <see cref="Result"/> class should be used instead.
    /// </remarks>
    public class BoolResult : ResultBase
    {
        /// <summary>
        /// Constructor for creating successful result instances.
        /// </summary>
        protected BoolResult()
        {
        }

        /// <nodoc />
        protected BoolResult(Error error)
            : base(error)
        {
        }

        /// <summary>
        /// Creates a new instance of a failed result.
        /// </summary>
        public BoolResult(string errorMessage, string? diagnostics = null)
            : base(Error.FromErrorMessage(errorMessage, diagnostics))
        {
            Contract.RequiresNotNullOrEmpty(errorMessage);
        }

        /// <summary>
        /// Creates a new instance of a failed result.
        /// </summary>
        public BoolResult(Exception exception, string? message = null)
            : base(exception, message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BoolResult" /> class.
        /// </summary>
        public BoolResult(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        /// <summary>
        /// Success singleton.
        /// </summary>
        public static readonly BoolResult Success = new BoolResult();

        /// <summary>
        /// Creates a successful result with the given diagnostic message as the success message
        /// </summary>
        public static BoolResult WithSuccessMessage(string successDiagnostics)
        {
            var result = new BoolResult();
            result.SetDiagnosticsForSuccess(successDiagnostics);
            return result;
        }

        /// <summary>
        /// Successful task singleton.
        /// </summary>
        public static readonly Task<BoolResult> SuccessTask = Task.FromResult(Success);

        /// <summary>
        /// Successful value task singleton.
        /// </summary>
        public static readonly ValueTask<BoolResult> SuccessValueTask = new ValueTask<BoolResult>(Success);

        /// <summary>
        /// Overloads &amp; operator to behave as AND operator.
        /// </summary>
        public static BoolResult operator &(BoolResult left, BoolResult right)
        {
            if (left.Succeeded)
            {
                return right;
            }

            if (right.Succeeded)
            {
                return left;
            }

            // We merge the errors the same way for '|' and '&' operators.
            return MergeFailures(left, right);
        }

        /// <summary>
        /// Overloads | operator to behave as OR operator.
        /// </summary>
        public static BoolResult operator |(BoolResult? left, BoolResult? right)
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
            return MergeFailures(left, right, defaultResultCtor: () => new BoolResult(),  fromError: error => new BoolResult(error));
        }

        private static BoolResult MergeFailures(BoolResult left, BoolResult right)
            => MergeFailures(left, right, defaultResultCtor: () => new BoolResult(), fromError: error => new BoolResult(error));

        /// <summary>
        /// Implicit conversion operator from <see cref="BoolResult"/> to <see cref="bool"/>.
        /// </summary>
        public static implicit operator bool(BoolResult result) => result.Succeeded;
    }
}
