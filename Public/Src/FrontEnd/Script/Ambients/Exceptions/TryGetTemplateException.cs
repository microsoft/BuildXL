// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Exception for template in context not available.
    /// </summary>
    public sealed class TryGetTemplateException : EvaluationExceptionWithErrorContext
    {
        private readonly Action<EvaluationErrors> m_logAction;

        /// <nodoc />
        private TryGetTemplateException(Action<EvaluationErrors> logAction, ErrorContext errorContext)
            : base("User error", errorContext)
        {
            m_logAction = logAction;
        }

        /// <nodoc />
        public static TryGetTemplateException TemplateNotAvailable(ModuleLiteral environment, ErrorContext errorContext, LineInfo lineInfo)
        {
            Contract.Requires(environment != null);

            Action<EvaluationErrors> logAction = errors => errors.ReportTemplateNotAvailable(environment, lineInfo);
            return new TryGetTemplateException(logAction, errorContext);
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            m_logAction(errors);
        }
    }
}
