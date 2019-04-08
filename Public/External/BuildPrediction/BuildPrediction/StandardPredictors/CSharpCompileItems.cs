// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors
{
    /// <summary>
    /// Finds Compile items, typically but not necessarily always from csproj files, as inputs.
    /// </summary>
    public class CSharpCompileItems : IProjectStaticPredictor
    {
        internal const string CompileItemName = "Compile";

        /// <inheritdoc/>
        public bool TryPredictInputsAndOutputs(
            Project project,
            ProjectInstance projectInstance,
            string repositoryRootDirectory,
            out StaticPredictions predictions)
        {
            // TODO: Need to determine how to normalize evaluated include selected below and determine if it is relative to project.

            List<BuildInput> itemInputs = project.GetItems(CompileItemName)
                .Select(item => new BuildInput(
                    Path.Combine(project.DirectoryPath, item.EvaluatedInclude),
                    isDirectory: false))
                .ToList();
            if (itemInputs.Count > 0)
            {
                predictions = new StaticPredictions(itemInputs, buildOutputDirectories: null);
                return true;
            }

            predictions = null;
            return false;
        }
    }
}
