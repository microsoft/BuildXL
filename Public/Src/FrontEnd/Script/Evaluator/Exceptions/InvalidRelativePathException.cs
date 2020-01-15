// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Exception that occurs when an ambient produces an invalid relative path.
    /// </summary>
    public sealed class InvalidRelativePathException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        public InvalidRelativePathException(string message, ErrorContext errorContext)
            : base(message, errorContext)
        {
            Contract.Requires(message != null);
        }

        /// <inheritdoc/>
        public override void ReportError(
            EvaluationErrors errors,
            ModuleLiteral environment,
            LineInfo location,
            Expression expression,
            Context context)
        {
            errors.ReportInvalidRelativePath(environment, ErrorContext, Message, location);
        }
    }
}
