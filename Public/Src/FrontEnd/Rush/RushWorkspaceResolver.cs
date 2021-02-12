// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
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
        protected override RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(m_context.StringTable, @"tools\RushGraphBuilder\main.js");

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
                if (AbsolutePath.TryCreate(m_context.PathTable, nonEscapedPath, out var absolutePath))
                {
                    if (m_host.Engine.FileExists(absolutePath.Combine(m_context.PathTable, "rush")))
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
            finalRushLibBaseLocation = foundPath.Combine(m_context.PathTable, 
                RelativePath.Create(m_context.StringTable, "node_modules/@microsoft/rush/node_modules"));

            // Just verbose log this
            Tracing.Logger.Log.UsingRushLibBaseAt(m_context.LoggingContext, resolverSettings.Location(m_context.PathTable), finalRushLibBaseLocation.ToString(m_context.PathTable));

            return true;
        }

        /// <summary>
        /// The graph construction tool expects: path-to-rush.json path-to-output-graph path-to-rush-lib
        /// </summary>
        protected override string GetGraphConstructionToolArguments(AbsolutePath outputFile, AbsolutePath toolLocation, AbsolutePath bxlGraphConstructionToolPath, string nodeExeLocation)
        {
            string pathToRushJson = m_resolverSettings.Root.Combine(m_context.PathTable, "rush.json").ToString(m_context.PathTable);

            return $@"/C """"{nodeExeLocation}"" ""{bxlGraphConstructionToolPath.ToString(m_context.PathTable)}"" ""{pathToRushJson}"" ""{outputFile.ToString(m_context.PathTable)}"" ""{toolLocation.ToString(m_context.PathTable)}""";
        }
    }
}
