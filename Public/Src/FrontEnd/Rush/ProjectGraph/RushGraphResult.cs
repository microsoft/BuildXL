// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// The result of computing a Rush graph contains the graph itself and the module definition that contains all the related specs
    /// </summary>
    public sealed class RushGraphResult : ISingleModuleProjectGraphResult
    {
        /// <nodoc/>
        public RushGraphResult(
            RushGraph rushGraph, 
            ModuleDefinition moduleDefinition, 
            IReadOnlyCollection<ResolvedRushExport> exports)
        {
            Contract.RequiresNotNull(rushGraph);
            Contract.RequiresNotNull(moduleDefinition);
            Contract.RequiresNotNull(exports);

            RushGraph = rushGraph;
            ModuleDefinition = moduleDefinition;
            Exports = exports;
        }

        /// <nodoc/>
        public ModuleDefinition ModuleDefinition { get; }

        /// <nodoc/>
        public IReadOnlyCollection<ResolvedRushExport> Exports { get; }

        /// <nodoc/>
        public RushGraph RushGraph { get; }
    }
}
