// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors.CopyTask
{
    /// <summary>
    /// Class for literal expressions, e.g. '$(Outdir)\foo.dll'.
    /// </summary>
    internal class FileExpressionLiteral : FileExpressionBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileExpressionLiteral"/> class.
        /// </summary>
        /// <param name="rawExpression">An unprocessed string for a single expression.</param>
        /// <param name="project">The project where the expression exists.</param>
        /// <param name="task">The task where the expression exists.</param>
        public FileExpressionLiteral(string rawExpression, ProjectInstance project, ProjectTaskInstance task = null)
            : base(rawExpression, project, task)
        {
            string expandedFileListString = project.ExpandString(ProcessedExpression);
            EvaluatedFiles = expandedFileListString.SplitStringList();
        }
    }
}
