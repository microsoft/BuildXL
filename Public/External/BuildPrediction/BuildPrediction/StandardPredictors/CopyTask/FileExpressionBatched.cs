// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text.RegularExpressions;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors.CopyTask
{
    /// <summary>
    /// Class for a batch expression, e.g. '%(Compile.fileName).%(Compile.extension))'.
    /// </summary>
    internal class FileExpressionBatched : FileExpressionBase
    {
        private static readonly Regex BatchedItemRegex = new Regex(@"%\((?<ItemType>[^\.\)]+)\.?(?<Metadata>[^\).]*?)\)", RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of the <see cref="FileExpressionBatched"/> class.
        /// </summary>
        /// <param name="rawExpression">An unprocessed string for a single expression.</param>
        /// <param name="project">The project where the expression exists.</param>
        /// <param name="task">The task where the expression exists.</param>
        public FileExpressionBatched(string rawExpression, ProjectInstance project, ProjectTaskInstance task = null)
            : base(rawExpression, project, task)
        {
            // Copy task has batching in it. Get the batched items if possible, then parse inputs.
            Match regexMatch = BatchedItemRegex.Match(ProcessedExpression);
            if (regexMatch.Success)
            {
                // If the user didn't specify a metadata item, then we default to Identity
                string transformItem =
                    string.IsNullOrEmpty(regexMatch.Groups[2].Value) ?
                        BatchedItemRegex.Replace(ProcessedExpression, @"%(Identity)") :
                        BatchedItemRegex.Replace(ProcessedExpression, @"%($2)");

                // Convert the batch into a transform. If this is an item -> metadata based transition then it will do the replacements for you.
                string expandedString = project.ExpandString(
                    $"@({regexMatch.Groups["ItemType"].Value}-> '{transformItem}')");
                EvaluatedFiles = expandedString.SplitStringList();
            }
            else
            {
                throw new InvalidOperationException();
            }
        }

        /// <summary>
        /// Determines if an expression is a batch expression.
        /// </summary>
        /// <param name="trimmedExpression">The Expression string with leading and trailing whitespace removed.</param>
        /// <returns>True if trimmedExpression is a batch Expression.</returns>
        public static bool IsExpression(string trimmedExpression)
        {
            return !FileExpressionTransform.IsExpression(trimmedExpression) &&
                   trimmedExpression.IndexOf("%(", StringComparison.Ordinal) >= 0;
        }
    }
}
