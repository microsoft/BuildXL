// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Yarn.ProjectGraph;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Yarn
{
    /// <summary>
    /// Workspace resolver for Yarn
    /// </summary>
    public class YarnWorkspaceResolver : ToolBasedJavaScriptWorkspaceResolver<YarnConfiguration, IYarnResolverSettings>
    {
        /// <summary>
        /// CODESYNC: the BuildXL deployment spec that places the tool
        /// </summary>
        protected override RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(Context.StringTable, @"tools\YarnGraphBuilder\main.js");

        /// <summary>
        /// Yarn graph needs Bxl to interpret how script commands are related to each other
        /// </summary>
        protected override bool ApplyBxlExecutionSemantics() => true;

        /// <inheritdoc/>
        public YarnWorkspaceResolver() : base(KnownResolverKind.YarnResolverKind)
        {
        }

        /// <inheritdoc/>
        protected override bool TryFindGraphBuilderToolLocation(IYarnResolverSettings resolverSettings, BuildParameters.IBuildParameters buildParameters, out AbsolutePath finalYarnLocation, out string failure)
        {
            // If the base location was provided at configuration time, we honor it as is
            string paths;

            var toolNameToFind = OperatingSystemHelper.IsWindowsOS ? new[] { "yarn.cmd" } : new[] { "yarn" };

            if (resolverSettings.YarnLocation != null)
            {
                var value = resolverSettings.YarnLocation.GetValue();
                if (value is FileArtifact file)
                {
                    finalYarnLocation = file;
                    failure = string.Empty;
                    return true;
                }
                else
                {
                    var pathCollection = ((IReadOnlyList<DirectoryArtifact>) value).Select(dir => dir.Path);
                    if (!FrontEndUtilities.TryFindToolInPath(Context, Host, pathCollection, toolNameToFind, out finalYarnLocation))
                    {
                        failure = $"'yarn' cannot be found under any of the provided paths '{string.Join(Path.PathSeparator.ToString(), pathCollection.Select(path => path.ToString(Context.PathTable)))}'.";
                        return false;
                    }

                    failure = string.Empty;
                    return true;
                }
            }

            // If the location was not provided, let's try to see if Yarn is under %PATH%
            paths = buildParameters["PATH"];

            if (!FrontEndUtilities.TryFindToolInPath(Context, Host, paths, toolNameToFind, out finalYarnLocation))
            {
                failure = "A location for 'yarn' is not explicitly specified. However, 'yarn' doesn't seem to be part of PATH. You can either specify the location explicitly using 'yarnLocation' field in " +
                    $"the Yarn resolver configuration, or make sure 'yarn' is part of your PATH. Current PATH is '{paths}'.";
                return false;
            }

            failure = string.Empty;

            // Just verbose log this
            Tracing.Logger.Log.UsingYarnAt(Context.LoggingContext, resolverSettings.Location(Context.PathTable), finalYarnLocation.ToString(Context.PathTable));

            return true;
        }

        /// <summary>
        /// The graph construction tool expects: path-to-repo-root path-to-output-graph path-to-yarn
        /// </summary>
        protected override string GetGraphConstructionToolArguments(AbsolutePath outputFile, AbsolutePath toolLocation, AbsolutePath bxlGraphConstructionToolPath, string nodeExeLocation)
        {
            // Node.exe sometimes misinterprets backslashes (e.g. "C:\" is interpreted such that \ is escaping quotes)
            // Use forward slashes for all node.exe arguments to avoid this.
            string pathToRepoRoot = ResolverSettings.Root.ToString(Context.PathTable, PathFormat.Script);

            var args = $@"""{nodeExeLocation}"" ""{bxlGraphConstructionToolPath.ToString(Context.PathTable, PathFormat.Script)}"" ""{pathToRepoRoot}"" ""{outputFile.ToString(Context.PathTable, PathFormat.Script)}"" ""{toolLocation.ToString(Context.PathTable, PathFormat.Script)}""";

            return JavaScriptUtilities.GetCmdArguments(args);
        }
    }
}
