// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.JavaScript.ProjectGraph
{
    /// <summary>
    /// The result of computing a JavaScript graph contains the graph itself and the module definition that contains all the related specs
    /// </summary>
    public sealed class JavaScriptGraphResult<TGraphConfiguration> : ISingleModuleProjectGraphResult where TGraphConfiguration: class
    {
        /// <nodoc/>
        public JavaScriptGraphResult(
            JavaScriptGraph<TGraphConfiguration> javaScriptGraph, 
            ModuleDefinition moduleDefinition, 
            IReadOnlyCollection<ResolvedJavaScriptExport> exports)
        {
            Contract.RequiresNotNull(javaScriptGraph);
            Contract.RequiresNotNull(moduleDefinition);
            Contract.RequiresNotNull(exports);

            JavaScriptGraph = javaScriptGraph;
            ModuleDefinition = moduleDefinition;
            Exports = exports;
        }

        /// <nodoc/>
        public ModuleDefinition ModuleDefinition { get; }

        /// <nodoc/>
        public IReadOnlyCollection<ResolvedJavaScriptExport> Exports { get; }

        /// <nodoc/>
        public JavaScriptGraph<TGraphConfiguration> JavaScriptGraph { get; }
    }
}
