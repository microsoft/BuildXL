// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// A value that is exposed to other resolvers to consume
    /// </summary>
    public readonly struct ResolvedRushExport
    {
        /// <nodoc/>
        public ResolvedRushExport(FullSymbol fullSymbol, IReadOnlyList<RushProject> exportedProjects)
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
        /// The collection of rush project that are part of <see cref="FullSymbol"/>
        /// </summary>
        public IReadOnlyCollection<RushProject> ExportedProjects { get; }
    }
}
