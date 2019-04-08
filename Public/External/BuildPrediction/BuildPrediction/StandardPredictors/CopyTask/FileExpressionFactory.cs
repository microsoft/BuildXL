// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors.CopyTask
{
    /// <summary>
    /// Factory class that instantiates FileExpression classes. File expressions are MSBuild expressions that
    /// evaluate to a list of files (e.g. @(Compile -> '%(filename)') or %(None.filename)).
    /// </summary>
    internal static class FileExpressionFactory
    {
        /// <summary>
        /// Factory method that parses an unprocessed Expression string and returns an Expression
        /// of the appropriate type.
        /// </summary>
        /// <param name="rawExpression">An unprocessed string for a single expression.</param>
        /// <param name="project">The project where the expression exists.</param>
        /// <param name="task">The task where the expression exists.</param>
        /// <returns>A file expression class.</returns>
        public static FileExpressionBase ParseExpression(string rawExpression, ProjectInstance project, ProjectTaskInstance task)
        {
            string trimmedExpression = rawExpression.Trim();

            if (FileExpressionBatched.IsExpression(trimmedExpression))
            {
                return new FileExpressionBatched(rawExpression, project, task);
            }

            if (FileExpressionTransform.IsExpression(rawExpression))
            {
                return new FileExpressionTransform(trimmedExpression, project, task);
            }

            return new FileExpressionLiteral(rawExpression, project, task);
        }
    }
}