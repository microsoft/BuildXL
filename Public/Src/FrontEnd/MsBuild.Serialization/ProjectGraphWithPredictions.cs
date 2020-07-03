// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// A project graph, where each node in the graph is decorated with a static prediction of inputs/outputs
    /// </summary>
    /// <remarks>
    /// This class is designed to be friendly to JSON serialization.
    /// </remarks>
    [JsonObject(IsReference = false)]
    public sealed class ProjectGraphWithPredictions<TPathType>
    {
        /// <nodoc/>
        public ProjectWithPredictions<TPathType>[] ProjectNodes { get; }

        /// <nodoc/>
        public ProjectGraphWithPredictions(ProjectWithPredictions<TPathType>[] projectNodes)
        {
            Contract.Requires(projectNodes != null);

            ProjectNodes = projectNodes;
        }
    }
}
