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
    public class BoolResult : ResultBase, IEquatable<BoolResult>
    {
        private readonly bool _succeeded;

        /// <summary>
        /// Creates new result instance with a given status.
        /// </summary>
        protected BoolResult(bool succeeded = true)
        {
            _succeeded = succeeded;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BoolResult"/> class.
        /// </summary>
        [Obsolete("Please use BoolResult(string, string) instead.")]
        protected BoolResult(bool succeeded, string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.RequiresNotNullOrEmpty(errorMessage);
            _succeeded = succeeded;
        }

        /// <summary>
        /// Creates a new instance of a failed result.
        /// </summary>
        public BoolResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
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

        /// <inheritdoc />
        public override bool Succeeded => _succeeded;

        /// <summary>
        /// Success singleton.
        /// </summary>
        public static readonly BoolResult Success = new BoolResult();

        /// <summary>
        /// Creates a successful result with the given diagnostic message as the success message
        /// </summary>
        public static BoolResult WithSuccessMessage(string successDiagnostics) => new BoolResult() { Diagnostics = successDiagnostics, PrintDiagnosticsForSuccess = true };

        /// <summary>
        /// Successful task singleton.
        /// </summary>
        public static readonly Task<BoolResult> SuccessTask = Task.FromResult(Success);

        /// <inheritdoc />
        public bool Equals(BoolResult? other)
        {
            return EqualsBase(other) && other != null && Succeeded == other.Succeeded;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is BoolResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Succeeded.GetHashCode() ^ (ErrorMessage?.GetHashCode() ?? 0);
        }

        /// <summary>
        /// Overloads &amp; operator to behave as AND operator.
        /// </summary>
        public static BoolResult operator &(BoolResult result1, BoolResult result2)
        {
            return result1.Succeeded
                ? result2
                : new BoolResult(
                    Merge(result1.ErrorMessage, result2.ErrorMessage, ", "),
                    Merge(result1.Diagnostics, result2.Diagnostics, ", "));
        }

        /// <summary>
        /// Overloads | operator to behave as OR operator.
        /// </summary>
        public static BoolResult operator |(BoolResult? result1, BoolResult? result2)
        {
            // One of the arguments may be null but not both.

            if (result1 == null)
            {
                Contract.AssertNotNull(result2);
                return result2;
            }

            if (result2 == null)
            {
                Contract.AssertNotNull(result1);
                return result1;
            }

            if (result1.Succeeded)
            {
                return result1;
            }

            if (result2.Succeeded)
            {
                return result2;
            }

            return new BoolResult(
                Merge(result1.ErrorMessage, result2.ErrorMessage, ", "),
                Merge(result1.Diagnostics, result2.Diagnostics, ", "));
        }

        /// <summary>
        /// Implicit conversion operator from <see cref="BoolResult"/> to <see cref="bool"/>.
        /// </summary>
        public static implicit operator bool(BoolResult result) => result.Succeeded;
    }
}
