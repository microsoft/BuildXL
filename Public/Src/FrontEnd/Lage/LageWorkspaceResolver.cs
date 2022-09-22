// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.Lage.ProjectGraph;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

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
            // Node.exe sometimes misinterprets backslashes (e.g. "C:\" is interpreted such that \ is escaping quotes)
            // Use forward slashes for all node.exe arguments to avoid this.
            string pathToRepoRoot = m_resolverSettings.Root.ToString(m_context.PathTable, PathFormat.Script);

            IEnumerable<string> commands = m_computedCommands.Keys;
            
            // Pass the 6th argument (lage location) as "undefined" string. This argument is used by Office implementation.
            var args = $@"""{nodeExeLocation}"" ""{bxlGraphConstructionToolPath.ToString(m_context.PathTable, PathFormat.Script)}"" ""{pathToRepoRoot}"" ""{outputFile.ToString(m_context.PathTable, PathFormat.Script)}"" ""{toolLocation.ToString(m_context.PathTable, PathFormat.Script)}"" ""{string.Join(" ", commands)}"" ""undefined""";
            
            return JavaScriptUtilities.GetCmdArguments(args);
        }
    }
}
