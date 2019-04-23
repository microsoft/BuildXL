// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients.Map
{
    /// <summary>
    /// Exception for null map key.
    /// </summary>
    public sealed class UndefinedMapKeyException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        public UndefinedMapKeyException(ErrorContext errorContext)
            : base("Undefined map key", errorContext)
        {
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportUndefinedMapKeyException(environment, ErrorContext, Message, location);
        }
    }
}
