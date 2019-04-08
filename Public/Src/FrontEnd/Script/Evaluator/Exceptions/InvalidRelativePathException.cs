// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
