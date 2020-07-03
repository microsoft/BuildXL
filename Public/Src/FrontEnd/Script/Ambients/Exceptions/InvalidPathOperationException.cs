// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients.Exceptions
{
    /// <summary>
    /// Exception that occurs due to invalid path operations.
    /// </summary>
    public sealed class InvalidPathOperationException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        public InvalidPathOperationException(string message, ErrorContext errorContext)
            : base(message, errorContext)
        {
            Contract.Requires(message != null);
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportInvalidPathOperation(environment, this, location);
        }
    }
}
