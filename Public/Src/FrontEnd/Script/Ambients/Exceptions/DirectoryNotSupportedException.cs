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
    /// Exception that occurs when an ambient encounters an error with a directory operation.
    /// </summary>
    public sealed class DirectoryNotSupportedException : EvaluationException
    {
        /// <summary>
        /// Directory name.
        /// </summary>
        public string Directory { get; }

        /// <nodoc />
        public DirectoryNotSupportedException(string directory)
        {
            Directory = directory;
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.DirectoryNotSupportedException(environment, expression, location);
        }
    }
}
