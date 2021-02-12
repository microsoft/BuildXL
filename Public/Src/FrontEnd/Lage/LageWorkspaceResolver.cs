// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Lage.ProjectGraph;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using System.Linq;
using System.Collections.Generic;
using BuildXL.FrontEnd.Utilities;

namespace BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// Workspace resolver for Lage
    /// </summary>
    public class LageWorkspaceResolver : ToolBasedJavaScriptWorkspaceResolver<LageConfiguration, ILageResolverSettings>
    {
        /// <summary>
        /// CODESYNC: the BuildXL deployment spec that places the tool
        /// </summary>
        protected override RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(m_context.StringTable, @"tools\LageGraphBuilder\main.js");

        /// <summary>
        /// Lage provides its own execution semantics
        /// </summary>
        protected override bool ApplyBxlExecutionSemantics() => false;

        /// <inheritdoc/>
        public LageWorkspaceResolver() : base(KnownResolverKind.LageResolverKind)
        {
        }

        /// <inheritdoc/>
        protected override bool TryFindGraphBuilderToolLocation(ILageResolverSettings resolverSettings, BuildParameters.IBuildParameters buildParameters, out AbsolutePath npmLocation, out string failure)
        {
            // If the base location was provided at configuration time, we honor it as is
            if (resolverSettings.NpmLocation.HasValue)
            {
                npmLocation = resolverSettings.NpmLocation.Value.Path;
                failure = string.Empty;
                return true;
            }

            // If the location was not provided, let's try to see if NPM is under %PATH%
            string paths = buildParameters["PATH"];

            if (!FrontEndUtilities.TryFindToolInPath(m_context, m_host, paths, new[] { "npm", "npm.cmd" }, out npmLocation))
            {
                failure = "A location for 'npm' is not explicitly specified. However, 'npm' doesn't seem to be part of PATH. You can either specify the location explicitly using 'npmLocation' field in " +
                    $"the Lage resolver configuration, or make sure 'npm' is part of your PATH. Current PATH is '{paths}'.";
                return false;
            }

            failure = string.Empty;

            // Just verbose log this
            Tracing.Logger.Log.UsingNpmAt(m_context.LoggingContext, resolverSettings.Location(m_context.PathTable), npmLocation.ToString(m_context.PathTable));

            return true;
        }

        /// <summary>
        /// The graph construction tool expects: path-to-repo-root path-to-output-graph path-to-npm commands-to-execute
        /// </summary>
        protected override string GetGraphConstructionToolArguments(AbsolutePath outputFile, AbsolutePath toolLocation, AbsolutePath bxlGraphConstructionToolPath, string nodeExeLocation)
        {
            string pathToRepoRoot = m_resolverSettings.Root.ToString(m_context.PathTable);

            IEnumerable<string> commands = m_resolverSettings.Execute.Select(command => command.GetCommandName());

            return $@"/C """"{nodeExeLocation}"" ""{bxlGraphConstructionToolPath.ToString(m_context.PathTable)}"" ""{pathToRepoRoot}"" ""{outputFile.ToString(m_context.PathTable)}"" ""{toolLocation.ToString(m_context.PathTable)} "" ""{string.Join(" ", commands)}""";
        }
    }
}
