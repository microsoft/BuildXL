// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Exception that occurs when an ambient produces an invalid path atom.
    /// </summary>
    public sealed class InvalidPathAtomException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        public InvalidPathAtomException(string message, ErrorContext errorContext)
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
            errors.ReportInvalidPathAtom(environment, ErrorContext, Message, location);
        }
    }
}
