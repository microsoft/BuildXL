// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// The result of computing an MsBuild static graph, together with its associated module definition so it becomes visible to the 
    /// frontend host controller
    /// </summary>
    public readonly struct ProjectGraphResult : ISingleModuleProjectGraphResult
    {
        /// <nodoc/>
        public ProjectGraphWithPredictions<AbsolutePath> ProjectGraph { get; }

        /// <nodoc/>
        public ModuleDefinition ModuleDefinition { get; }

        /// <nodoc/>
        public AbsolutePath MsBuildLocation { get; }

        /// <nodoc/>
        public AbsolutePath DotNetExeLocation { get; }

        /// <nodoc/>
        public ProjectGraphResult(ProjectGraphWithPredictions<AbsolutePath> projectGraphWithPredictions, ModuleDefinition moduleDefinition, AbsolutePath msBuildLocation, AbsolutePath dotnetExeLocation)
        {
            Contract.Requires(projectGraphWithPredictions != null);
            Contract.Requires(moduleDefinition != null);
            Contract.Requires(msBuildLocation.IsValid);

            ProjectGraph = projectGraphWithPredictions;
            ModuleDefinition = moduleDefinition;
            MsBuildLocation = msBuildLocation;
            DotNetExeLocation = dotnetExeLocation;
        }
    }
}
