// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Exception that occurs when a user input is invalid.
    /// </summary>
    public sealed class InputValidationException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        public InputValidationException(string message, ErrorContext errorContext)
            : base(message, errorContext)
        {
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportInputValidationException(environment, this, location);
        }
    }
}
