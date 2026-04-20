// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.JavaScript.ProjectGraph
{
    /// <summary>
    /// A value that is exposed to other resolvers to consume
    /// </summary>
    public readonly struct ResolvedJavaScriptExport
    {
        /// <nodoc/>
        public ResolvedJavaScriptExport(FullSymbol fullSymbol, IReadOnlyList<JavaScriptProject> exportedProjects, bool includeProjectMapping = false)
        {
            Contract.RequiresNotNull(exportedProjects);
            
            FullSymbol = fullSymbol;
            ExportedProjects = exportedProjects;
            IncludeProjectMapping = includeProjectMapping;
        }

        /// <summary>
        /// Symbol to be exposed to other resolvers
        /// </summary>
        public FullSymbol FullSymbol {get;}
        
        /// <summary>
        /// The collection of projects that are part of <see cref="FullSymbol"/>
        /// </summary>
        public IReadOnlyCollection<JavaScriptProject> ExportedProjects { get; }

        /// <summary>
        /// When true, the export type becomes Map&lt;JavaScriptProjectIdentifier, StaticDirectory[]&gt; instead of StaticDirectory[].
        /// </summary>
        public bool IncludeProjectMapping { get; }
    }
}
