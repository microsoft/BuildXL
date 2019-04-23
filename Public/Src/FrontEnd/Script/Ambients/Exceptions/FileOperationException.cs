// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Exception that occurs when an ambient encounters an error with a file operation.
    /// </summary>
    public sealed class FileOperationException : EvaluationException
    {
        /// <summary>
        /// Wrapped exception.
        /// </summary>
        public Exception WrappedException { get; }

        /// <nodoc />
        public FileOperationException(Exception wrappedException)
        {
            Contract.Requires(wrappedException != null);
            WrappedException = wrappedException;
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            string additionalInformation =
                string.IsNullOrEmpty(WrappedException.Message)
                    ? string.Empty
                    : I($" : {WrappedException.Message}");

            errors.ReportFileOperationError(environment, expression, additionalInformation, location);
        }
    }
}
