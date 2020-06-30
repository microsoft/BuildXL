// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.JavaScript.ProjectGraph
{
    /// <summary>
    /// A value that is exposed to other resolvers to consume
    /// </summary>
    public readonly struct ResolvedJavaScriptExport
    {
        /// <nodoc/>
        public ResolvedJavaScriptExport(FullSymbol fullSymbol, IReadOnlyList<JavaScriptProject> exportedProjects)
        {
            Contract.RequiresNotNull(exportedProjects);
            
            FullSymbol = fullSymbol;
            ExportedProjects = exportedProjects;
        }

        /// <summary>
        /// Symbol to be exposed to other resolvers
        /// </summary>
        public FullSymbol FullSymbol {get;}
        
        /// <summary>
        /// The collection of projects that are part of <see cref="FullSymbol"/>
        /// </summary>
        public IReadOnlyCollection<JavaScriptProject> ExportedProjects { get; }
    }
}
