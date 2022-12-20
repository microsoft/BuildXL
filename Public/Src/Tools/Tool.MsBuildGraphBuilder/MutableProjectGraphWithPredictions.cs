// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.MsBuild.Serialization;

namespace ProjectGraphBuilder
{
    /// <summary>
    /// Mutable version of <see cref="ProjectGraphWithPredictions"/>
    /// </summary>
    internal class MutableProjectGraphWithPredictions
    {
        /// <nodoc/>
        public MutableProjectWithPredictions[] ProjectNodes { get; }

        /// <nodoc/>
        public MutableProjectGraphWithPredictions(MutableProjectWithPredictions[] projectNodes)
        {
            Contract.Requires(projectNodes != null);

            ProjectNodes = projectNodes;
        }

        /// <nodoc/>
        public ProjectGraphWithPredictions<string> ToImmutable()
        {
            var mutableToImmutableMap = ProjectNodes.ToDictionary(
                n => n,
                n => new ProjectWithPredictions<string>(
                    n.FullPath,
                    n.ImplementsTargetProtocol,
                    n.GlobalProperties,
                    n.PredictedInputFiles.ToArray(),
                    n.PredictedOutputFolders.ToArray(),
                    n.PredictedTargetsToExecute));
            var immutableToMutableMap = mutableToImmutableMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
            foreach (var kvp in immutableToMutableMap)
            {
                ProjectWithPredictions<string> immutable = kvp.Key;
                MutableProjectWithPredictions mutable = kvp.Value;
                immutable.SetDependencies(mutable.Dependencies.Select(d => mutableToImmutableMap[d]).ToArray());
            }

            return new ProjectGraphWithPredictions<string>(immutableToMutableMap.Keys.ToArray());
        }
    }
}
