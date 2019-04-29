// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Exception for null map key.
    /// </summary>
    public sealed class TryGetMountException : EvaluationExceptionWithErrorContext
    {
        private readonly Action<EvaluationErrors> m_logAction;

        /// <nodoc />
        private TryGetMountException(Action<EvaluationErrors> logAction, ErrorContext errorContext)
            : base("User error", errorContext)
        {
            m_logAction = logAction;
        }

        /// <nodoc />
        public static TryGetMountException MountNameNullOrEmpty(ModuleLiteral environment, ErrorContext errorContext, LineInfo lineInfo)
        {
            Contract.Requires(environment != null);

            Action<EvaluationErrors> logAction = errors => errors.ReportGetMountNameNullOrEmpty(environment, lineInfo);
            return new TryGetMountException(logAction, errorContext);
        }

        /// <nodoc />
        public static TryGetMountException MountNameCaseMismatch(ModuleLiteral environment, string name, string mountName, ErrorContext errorContext, LineInfo lineInfo)
        {
            Contract.Requires(environment != null);

            Action<EvaluationErrors> logAction = errors => errors.ReportGetMountNameCaseMisMatch(environment, name, mountName, lineInfo: lineInfo);
            return new TryGetMountException(logAction, errorContext);
        }

        /// <nodoc />
        public static TryGetMountException MountNameNotFound(ModuleLiteral environment, string name, IEnumerable<string> mountNames, ErrorContext errorContext, LineInfo lineInfo)
        {
            Contract.Requires(environment != null);

            Action<EvaluationErrors> logAction = errors => errors.ReportGetMountNameNotFound(environment, name, mountNames, lineInfo: lineInfo);
            return new TryGetMountException(logAction, errorContext);
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            m_logAction(errors);
        }
    }
}
