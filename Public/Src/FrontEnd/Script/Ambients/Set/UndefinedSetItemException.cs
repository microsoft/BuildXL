// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients.Set
{
    /// <summary>
    /// Exception for null map key.
    /// </summary>
    public sealed class UndefinedSetItemException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        public UndefinedSetItemException(ErrorContext errorContext)
            : base("Undefined set item", errorContext)
        {
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportUndefinedSetItem(environment, ErrorContext, Message, location);
        }
    }
}
