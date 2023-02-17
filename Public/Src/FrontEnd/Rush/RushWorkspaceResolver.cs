// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Workspace resolver for Rush
    /// </summary>
    public class RushWorkspaceResolver : ToolBasedJavaScriptWorkspaceResolver<RushConfiguration, RushResolverSettings>
    {
        /// <summary>
        /// CODESYNC: the BuildXL deployment spec that places the tool
        /// </summary>
        protected override RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(Context.StringTable, @"tools\RushGraphBuilder\main.js");

        /// <summary>
        /// Rush graph needs Bxl to interpret how script commands are related to each other
        /// </summary>
        protected override bool ApplyBxlExecutionSemantics() => true;

        /// <inheritdoc/>
        public RushWorkspaceResolver() : base(KnownResolverKind.RushResolverKind)
        {
        }

        /// <inheritdoc/>
        protected override bool TryFindGraphBuilderToolLocation(RushResolverSettings resolverSettings, BuildParameters.IBuildParameters buildParameters, out AbsolutePath finalRushLibBaseLocation, out string failure)
        {
            // If the base location was provided at configuration time, we honor it as is
            if (resolverSettings.RushLibBaseLocation.HasValue)
            {
                finalRushLibBaseLocation = resolverSettings.RushLibBaseLocation.Value.Path;
                failure = string.Empty;
                return true;
            }

            finalRushLibBaseLocation = AbsolutePath.Invalid;

            // If the location was not provided, let's try to see if Rush is installed, since rush-lib comes as part of it
            // Look in %PATH% (as exposed in build parameters) for rush
            string paths = buildParameters["PATH"];

            AbsolutePath foundPath = AbsolutePath.Invalid;
            foreach (string path in paths.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                var nonEscapedPath = path.Trim('"');
                // Sometimes PATH is not well-formed, so make sure we can actually recognize an absolute path there
                if (AbsolutePath.TryCreate(Context.PathTable, nonEscapedPath, out var absolutePath))
                {
                    if (Host.Engine.FileExists(absolutePath.Combine(Context.PathTable, "rush")))
                    {
                        foundPath = absolutePath;
                        break;
                    }
                }
            }

            if (!foundPath.IsValid)
            {
                failure = "A location for 'rush-lib' is not explicitly specified, so trying to find a Rush installation to use instead. " +
                    "However, 'rush' doesn't seem to be part of PATH. You can either specify the location explicitly using 'rushLibBaseLocation' field in " +
                    $"the Rush resolver configuration, or make sure 'rush' is part of your PATH. Current PATH is '{paths}'.";
                return false;
            }

            // We found where Rush is located. So rush-lib is a known dependency of it, so should be nested within Rush module
            // Observe that even if that's not the case the final validation will occur under the rush graph builder tool, when
            // the module is tried to be loaded
            failure = string.Empty;
            finalRushLibBaseLocation = foundPath.Combine(Context.PathTable, 
                RelativePath.Create(Context.StringTable, "node_modules/@microsoft/rush/node_modules"));

            // Just verbose log this
            Tracing.Logger.Log.UsingRushLibBaseAt(Context.LoggingContext, resolverSettings.Location(Context.PathTable), finalRushLibBaseLocation.ToString(Context.PathTable));

            return true;
        }

        /// <summary>
        /// The graph construction tool expects: path-to-rush.json path-to-output-graph path-to-rush-lib
        /// </summary>
        protected override string GetGraphConstructionToolArguments(AbsolutePath outputFile, AbsolutePath toolLocation, AbsolutePath bxlGraphConstructionToolPath, string nodeExeLocation)
        {
            // Node.exe sometimes misinterprets backslashes (e.g. "C:\" is interpreted such that \ is escaping quotes)
            // Use forward slashes for all node.exe arguments to avoid this.
            string pathToRushJson = ResolverSettings.Root.Combine(Context.PathTable, "rush.json").ToString(Context.PathTable, PathFormat.Script);

            var args = $@"""{nodeExeLocation}"" ""{bxlGraphConstructionToolPath.ToString(Context.PathTable, PathFormat.Script)}"" ""{pathToRushJson}"" ""{outputFile.ToString(Context.PathTable, PathFormat.Script)}"" ""{toolLocation.ToString(Context.PathTable, PathFormat.Script)}""";
            return JavaScriptUtilities.GetCmdArguments(args);
        }
    }
}
