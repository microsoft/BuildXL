// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Lage.ProjectGraph;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// Workspace resolver for Lage
    /// </summary>
    public class LageWorkspaceResolver : JavaScriptWorkspaceResolver<LageConfiguration, ILageResolverSettings>
    {
        /// <summary>
        /// CODESYNC: the BuildXL deployment spec that places the tool
        /// </summary>
        protected override RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(m_context.StringTable, @"tools\LageGraphBuilder\main.js");

        /// <inheritdoc/>
        public LageWorkspaceResolver() : base(KnownResolverKind.LageResolverKind)
        {
        }

        /// <inheritdoc/>
        protected override bool TryFindGraphBuilderToolLocation(ILageResolverSettings resolverSettings, BuildParameters.IBuildParameters buildParameters, out AbsolutePath finalLageLocation, out string failure)
        {
            // We use an npm call to invoke Lage
            finalLageLocation = AbsolutePath.Invalid;
            failure = null;
            return true;
        }

        /// <summary>
        /// The graph construction tool expects: path-to-repo-root path-to-output-graph path-to-Lage
        /// </summary>
        protected override string GetGraphConstructionToolArguments(AbsolutePath outputFile, AbsolutePath toolLocation, AbsolutePath bxlGraphConstructionToolPath, string nodeExeLocation)
        {
            string pathToRepoRoot = m_resolverSettings.Root.ToString(m_context.PathTable);

            return $@"/C """"{nodeExeLocation}"" ""{bxlGraphConstructionToolPath.ToString(m_context.PathTable)}"" ""{pathToRepoRoot}"" ""{outputFile.ToString(m_context.PathTable)}"" ""{string.Join(" ", m_resolverSettings.Targets)}""";
        }
    }
}
