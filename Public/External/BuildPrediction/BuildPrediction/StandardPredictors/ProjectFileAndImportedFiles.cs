// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace Microsoft.Build.Prediction.StandardPredictors
{
    /// <summary>
    /// Finds project filename and imports, as inputs.
    /// </summary>
    public class ProjectFileAndImportedFiles : IProjectStaticPredictor
    {
        /// <inheritdoc/>
        public bool TryPredictInputsAndOutputs(Project project, ProjectInstance projectInstance, string repositoryRootDirectory, out StaticPredictions predictions)
        {
            var inputs = new List<BuildInput>()
            {
                new BuildInput(project.FullPath, false)
            };

            foreach (ResolvedImport import in project.Imports)
            {
                inputs.Add(new BuildInput(import.ImportedProject.FullPath, isDirectory: false));
            }

            predictions = new StaticPredictions(inputs, null);

            return true;
        }
    }
}
