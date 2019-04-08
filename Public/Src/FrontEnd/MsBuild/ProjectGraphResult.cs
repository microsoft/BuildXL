// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.MsBuild.Serialization;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// The result of computing an MsBuild static graph, together with its associated module definition so it becomes visible to the 
    /// frontend host controller
    /// </summary>
    public readonly struct ProjectGraphResult
    {
        /// <nodoc/>
        public ProjectGraphWithPredictions<AbsolutePath> ProjectGraph { get; }

        /// <nodoc/>
        public ModuleDefinition ModuleDefinition { get; }

        /// <nodoc/>
        public AbsolutePath MsBuildExeLocation { get; }

        /// <nodoc/>
        public ProjectGraphResult(ProjectGraphWithPredictions<AbsolutePath> projectGraphWithPredictions, ModuleDefinition moduleDefinition, AbsolutePath msBuildExeLocation)
        {
            Contract.Requires(projectGraphWithPredictions != null);
            Contract.Requires(moduleDefinition != null);
            Contract.Requires(msBuildExeLocation.IsValid);

            ProjectGraph = projectGraphWithPredictions;
            ModuleDefinition = moduleDefinition;
            MsBuildExeLocation = msBuildExeLocation;
        }
    }
}
