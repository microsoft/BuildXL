// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Base class for all evaluation-based exceptions.
    /// </summary>
    /// <remarks>
    /// This type builds a common infrastructure for error handling during evaluation.
    /// Each error captures some context required for appropriate error handling.
    /// But in most cases error the only possible "handling" of an error is logging.
    /// This aspect built-in into this type via <see cref="ReportError"/> function
    /// that should be called from a catch block for logging purposes.
    /// </remarks>
    public abstract class EvaluationException : Exception
    {
        /// <nodoc/>
        protected EvaluationException()
        { }

        /// <nodoc/>
        protected EvaluationException(string message)
            : base(message)
        { }

        /// <nodoc/>
        protected EvaluationException(string message, Exception innerException)
            : base(message, innerException)
        { }

        /// <summary>
        /// Logs error via <paramref name="errors"/>.
        /// </summary>
        public abstract void ReportError(
            EvaluationErrors errors,
            ModuleLiteral environment,
            LineInfo location,
            Expression expression,
            Context context);
    }

    /// <summary>
    /// Exception with error context.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public abstract class EvaluationExceptionWithErrorContext : EvaluationException
    {
        /// <summary>
        /// Error context.
        /// </summary>
        public ErrorContext ErrorContext { get; }

        /// <nodoc />
        protected EvaluationExceptionWithErrorContext(string message, ErrorContext errorContext)
            : base(message)
        {
            Contract.Requires(message != null);

            ErrorContext = errorContext;
        }

        /// <nodoc />
        protected EvaluationExceptionWithErrorContext(ErrorContext errorContext)
        {
            ErrorContext = errorContext;
        }
    }
}
