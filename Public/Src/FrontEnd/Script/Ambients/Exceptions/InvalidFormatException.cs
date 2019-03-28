// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Exception that occurs when converting a string to a value.
    /// </summary>
    public sealed class InvalidFormatException : EvaluationExceptionWithErrorContext
    {
        /// <summary>
        /// Target type.
        /// </summary>
        public Type TargetType { get; }

        /// <nodoc />
        public InvalidFormatException(Type targetType, string message, ErrorContext errorContext)
            : base(message, errorContext)
        {
            Contract.Requires(targetType != null);
            Contract.Requires(message != null);

            TargetType = targetType;
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportInvalidTypeFormat(environment, this, location);
        }
    }
}
