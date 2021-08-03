// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Factory methods for constructing <see cref="Result{T}"/>.
    /// </summary>
    public partial class Result
    {
        private static readonly Result _successResult = new Result();
        private static readonly Task<Result> _successTask = Task.FromResult(_successResult);

        /// <summary>
        /// Creates a successful result with the given diagnostic message as the success message
        /// </summary>
        public static Result WithMessage(string successDiagnostics)
        {
            var result = new Result(successDiagnostics);
            result.SetDiagnosticsForSuccess(successDiagnostics);
            return result;
        }

        /// <nodoc />
        public static Result<T> Success<T>(T result, bool isNullAllowed = false) => new Result<T>(result, isNullAllowed);

        /// <summary>
        /// Returns a singleton instance for successful operation.
        /// </summary>
        public static Result Success() => _successResult;

        /// <summary>
        /// Returns a singleton instance for a successful asynchronous operation.
        /// </summary>
        public static Task<Result> SuccessTask() => _successTask;

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
}
