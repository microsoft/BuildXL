// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors.CopyTask
{
    /// <summary>
    /// Contains a parsed list of file expressions as well as the list of files derived from evaluating said
    /// expressions.
    /// </summary>
    internal class FileExpressionList
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileExpressionList"/> class.
        /// </summary>
        /// <param name="rawFileListString">The unprocessed list of file expressions.</param>
        /// <param name="project">The project where the expression list exists.</param>
        /// <param name="task">The task where the expression list exists.</param>
        public FileExpressionList(string rawFileListString, ProjectInstance project, ProjectTaskInstance task)
        {
            IList<string> expressions = rawFileListString.SplitStringList();
            var seenFiles = new HashSet<string>(PathComparer.Instance);
            foreach (string expression in expressions)
            {
                FileExpressionBase parsedExpression = FileExpressionFactory.ParseExpression(expression, project, task);
                Expressions.Add(parsedExpression);

                foreach (string file in parsedExpression.EvaluatedFiles)
                {
                    if (string.IsNullOrWhiteSpace(file))
                    {
                        continue;
                    }

                    if (seenFiles.Add(file))
                    {
                        DedupedFiles.Add(file);
                    }

                    AllFiles.Add(file);
                }
            }
        }

        /// <summary>
        /// Gets the set of all expressions in the file list string.
        /// </summary>
        public List<FileExpressionBase> Expressions { get; } = new List<FileExpressionBase>();

        /// <summary>
        /// Gets the set of all files in all of the expanded expressions. May include duplicates.
        /// </summary>
        public List<string> AllFiles { get; } = new List<string>();

        /// <summary>
        /// Gets the set of all files in the expanded expressions. Duplicates are removed.
        /// </summary>
        public List<string> DedupedFiles { get; } = new List<string>();

        /// <summary>
        /// Gets the number of batch expressions in the file list.
        /// </summary>
        public int NumBatchExpressions => Expressions.Count((FileExpressionBase expression) => expression.GetType() == typeof(FileExpressionBatched));

        /// <summary>
        /// Gets the number of literal expressions in the file list.
        /// </summary>
        public int NumLiteralExpressions => Expressions.Count((FileExpressionBase expression) => expression.GetType() == typeof(FileExpressionLiteral));

        /// <summary>
        /// Gets the number of transform expressions in the file list.
        /// </summary>
        public int NumTransformExpressions => Expressions.Count((FileExpressionBase expression) => expression.GetType() == typeof(FileExpressionTransform));
    }
}
