// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors.CopyTask
{
    /// <summary>
    /// Class for transform expressions, e.g. @(Compile -> '$(Outdir)\$(filename)') .
    /// </summary>
    internal class FileExpressionTransform : FileExpressionLiteral
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileExpressionTransform"/> class.
        /// </summary>
        /// <param name="rawExpression">An unprocessed string for a single expression.</param>
        /// <param name="project">The project where the expression exists.</param>
        /// <param name="task">The task where the expression exists.</param>
        public FileExpressionTransform(string rawExpression, ProjectInstance project, ProjectTaskInstance task = null)
            : base(rawExpression, project, task)
        {
        }

        /// <summary>
        /// Determines if an expression is a transform expression.
        /// </summary>
        /// <param name="trimmedExpression">The Expression string with leading and trailing whitespace removed.</param>
        /// <returns>True if trimmedExpression is a transform Expression.</returns>
        public static bool IsExpression(string trimmedExpression)
        {
            return trimmedExpression.StartsWith("@(", StringComparison.Ordinal);
        }
    }
}