// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Workspace resolver for Rush
    /// </summary>
    public class RushWorkspaceResolver : ToolBasedJavaScriptWorkspaceResolver<RushConfiguration, RushResolverSettings>
    {
        private bool m_useRushBuildGraphPlugin;

        /// <summary>
        /// CODESYNC: the BuildXL deployment spec that places the tool
        /// </summary>
        protected override RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(Context.StringTable, @"tools\RushGraphBuilder\main.js");

        /// <summary>
        /// When using rush-lib, the result graph needs Bxl to interpret how script commands are related to each other. Otherwise, when using @rushstack/rush-build-graph-plugin, 
        /// the graph is already defined at the script-to-script level.
        /// </summary>
        protected override bool ApplyBxlExecutionSemantics() => !m_useRushBuildGraphPlugin;

        /// <inheritdoc/>
        public RushWorkspaceResolver() : base(KnownResolverKind.RushResolverKind)
        {
        }

        /// <inheritdoc/>
        public override bool TryInitialize(FrontEndHost host, FrontEndContext context, IConfiguration configuration, IResolverSettings resolverSettings)
        {
            var rushResolverSettings = resolverSettings as IRushResolverSettings;
            Contract.Requires(rushResolverSettings != null);

            // Whether we need to use rush-build-graph-plugin or rush-lib is driven by the configuration.
            m_useRushBuildGraphPlugin = ShouldUseRushBuildGraphPlugin(rushResolverSettings);

            return base.TryInitialize(host, context, configuration, resolverSettings);
        }

        /// <inheritdoc/>
        protected override bool TryFindGraphBuilderToolLocation(RushResolverSettings resolverSettings, BuildParameters.IBuildParameters buildParameters, out AbsolutePath finalLocation, out string failure)
        {
            failure = string.Empty;

            // If the rush-lib location was provided at configuration time, we honor it as is
            if (resolverSettings.RushLibBaseLocation.HasValue)
            {
                finalLocation = resolverSettings.RushLibBaseLocation.Value.Path;
                return true;
            }

            // If the rush location was provided at configuration time, we honor it as is
            if (resolverSettings.RushLocation.HasValue)
            {
                finalLocation = resolverSettings.RushLocation.Value.Path;
                return true;
            }

            finalLocation = AbsolutePath.Invalid;

            // If the location was not provided, let's try to see if Rush is installed
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
                failure = $"A location for ${(m_useRushBuildGraphPlugin ? "'rush'" : "'rush-lib'")} is not explicitly specified, so trying to find a Rush installation to use instead. " +
                    $"However, 'rush' doesn't seem to be part of PATH. You can either specify the location explicitly using ${(m_useRushBuildGraphPlugin ? "'rushLocation'" : "'rushLibBaseLocation'")} field in " +
                    $"the Rush resolver configuration, or make sure 'rush' is part of your PATH. Current PATH is '{paths}'.";
                return false;
            }

            if (m_useRushBuildGraphPlugin) 
            {
                // rush-build-graph mode uses rush directly, so just return the location.
                finalLocation = foundPath;
            }
            else
            {
                // We found where Rush is located. rush-lib is a known dependency of it, so should be nested within Rush module
                // Observe that even if that's not the case the final validation will occur under the rush graph builder tool, when
                // the module is tried to be loaded
                failure = string.Empty;
                finalLocation = foundPath.Combine(Context.PathTable,
                    RelativePath.Create(Context.StringTable, "node_modules/@microsoft/rush/node_modules"));

                // Just verbose log this
                Tracing.Logger.Log.UsingRushLibBaseAt(Context.LoggingContext, resolverSettings.Location(Context.PathTable), finalLocation.ToString(Context.PathTable));
            }

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

            var args = $"\"{nodeExeLocation}\" \"{bxlGraphConstructionToolPath.ToString(Context.PathTable, PathFormat.Script)}\" \"{pathToRushJson}\" " +
                $"\"{outputFile.ToString(Context.PathTable, PathFormat.Script)}\" \"{toolLocation.ToString(Context.PathTable, PathFormat.Script)}\" \"{m_useRushBuildGraphPlugin}\"";
            
            return JavaScriptUtilities.GetCmdArguments(args);
        }

        private static bool ShouldUseRushBuildGraphPlugin(IRushResolverSettings resolverSettings)
        {
            // If the graph construction mode is specified, we just honor that
            switch (resolverSettings.GraphConstructionMode)
            {
                case "rush-lib":
                    return false;
                case "rush-build-graph":
                    return true;
                case null:
                    break;
                default:
                    // This should never happen, since the DScript type checker should have caught this
                    throw new InvalidOperationException($"Invalid value for 'graphConstructionMode' field in Rush resolver settings: '{resolverSettings.GraphConstructionMode}'.");
            }

            // If the rush location is specified, we use rush-build-graph
            if (resolverSettings.RushLocation != null)
            {
                return true;
            }

            // If the rush-lib location is specified, we use rush-lib
            if (resolverSettings.RushLibBaseLocation != null)
            {
                return false;
            }

            // If we reached this point, we don't have enough information to decide, so we default to rush-lib
            // Observe that passing execute arguments that include dependencies is the remaining distinction between the two modes, but since including dependencies means rush-lib, which is also the default
            // if we don't have enough information, there is no point in checking that.
            return false;
        }   
    }
}
