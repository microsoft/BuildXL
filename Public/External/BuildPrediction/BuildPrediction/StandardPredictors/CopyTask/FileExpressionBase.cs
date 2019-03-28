// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors.CopyTask
{
    /// <summary>
    /// The abstract base class of an MSBuild item expression (e.g. @(Compile), %(Content.Filename), etc.)
    /// </summary>
    internal abstract class FileExpressionBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileExpressionBase"/> class.
        /// </summary>
        /// <param name="rawExpression">An unprocessed string for a single expression.</param>
        /// <param name="project">The project where the expression exists.</param>
        /// <param name="task">The task where the expression exists.</param>
        protected FileExpressionBase(string rawExpression, ProjectInstance project, ProjectTaskInstance task = null)
        {
            string trimmedExpression = rawExpression.Trim();

            if (task != null)
            {
                string copyTaskFilePath = task.Location.File;

                // Process MsBuildThis* macros
                // ignore copy tasks within the proj - evaluation will just work.
                if (!copyTaskFilePath.Equals(project.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    // We leave off the trailing ')' to allow for macro operations. This could allow us to misdetect macros
                    // (e.g. $(MsBuildThisFileButNotReally), but should be rare and should still function correctly even if we
                    // do.
                    if (trimmedExpression.IndexOf("$(MSBuildThisFile", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
#pragma warning disable CA1308 // Normalize strings to uppercase
                        trimmedExpression = trimmedExpression.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
                        trimmedExpression = trimmedExpression.Replace("$(msbuildthisfiledirectory)", Path.GetDirectoryName(copyTaskFilePath) + "\\");
                        trimmedExpression = trimmedExpression.Replace("$(msbuildthisfile)", Path.GetFileName(copyTaskFilePath));
                        trimmedExpression = trimmedExpression.Replace("$(msbuildthisfileextension)", Path.GetExtension(copyTaskFilePath));
                        trimmedExpression = trimmedExpression.Replace("$(msbuildthisfilefullpath)", copyTaskFilePath);
                        trimmedExpression = trimmedExpression.Replace("$(msbuildthisfilename)", Path.GetFileNameWithoutExtension(copyTaskFilePath));
                    }
                }
            }

            ProcessedExpression = trimmedExpression;
        }

        /// <summary>
        /// Gets or sets the set of all files in the evaluated expression.
        /// </summary>
        public IEnumerable<string> EvaluatedFiles { get; protected set; }

        protected string ProcessedExpression { get; }
    }
}
