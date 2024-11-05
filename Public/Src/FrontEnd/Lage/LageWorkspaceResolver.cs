// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Lage.ProjectGraph;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// Workspace resolver for Lage
    /// </summary>
    public class LageWorkspaceResolver : ToolBasedJavaScriptWorkspaceResolver<LageConfiguration, LageResolverSettings>
    {
        private bool m_useNpmLocation = true;

        /// <summary>
        /// CODESYNC: the BuildXL deployment spec that places the tool
        /// </summary>
        protected override RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(Context.StringTable, @"tools\LageGraphBuilder\main.js");

        /// <summary>
        /// Lage provides its own execution semantics
        /// </summary>
        protected override bool ApplyBxlExecutionSemantics() => false;

        /// <inheritdoc/>
        public LageWorkspaceResolver() : base(KnownResolverKind.LageResolverKind)
        {
        }

        /// <inheritdoc/>
        protected override bool TryFindGraphBuilderToolLocation(LageResolverSettings resolverSettings, BuildParameters.IBuildParameters buildParameters, out AbsolutePath toolLocation, out string failure)
        {
            if (resolverSettings.NpmLocation.HasValue && resolverSettings.LageLocation.HasValue)
            {
                failure = "npmLocation and lageLocation cannot be specified simultaneously.";
                toolLocation = AbsolutePath.Invalid;
                return false;
            }

            // If the npm location was provided at configuration time, we honor it as is
            if (resolverSettings.NpmLocation.HasValue)
            {
                toolLocation = resolverSettings.NpmLocation.Value.Path;
                failure = string.Empty;
                return true;
            }

            // Same for Lage location
            if (resolverSettings.LageLocation.HasValue)
            {
                toolLocation = resolverSettings.LageLocation.Value.Path;
                failure = string.Empty;
                // Indicate that we are using the lage location, since the graph construction tool arguments will be different
                m_useNpmLocation = false;
                return true;
            }

            // When we reach this point, we know that neither npm nor lage location was provided. Let's try to find npm in PATH.
            // We could try to look for lage as well (after npm, for example), but they are usually both there - or none of them are.
            string paths = buildParameters["PATH"];

            if (!FrontEndUtilities.TryFindToolInPath(Context, Host, paths, new[] { "npm", "npm.cmd" }, out toolLocation))
            {
                failure = "A location for 'npm' is not explicitly specified. However, 'npm' doesn't seem to be part of PATH. You can either specify the location explicitly using 'npmLocation' field in " +
                    $"the Lage resolver configuration, or make sure 'npm' is part of your PATH. Current PATH is '{paths}'.";
                return false;
            }

            failure = string.Empty;

            // Just verbose log this
            Tracing.Logger.Log.UsingToolAt(Context.LoggingContext, resolverSettings.Location(Context.PathTable), toolLocation.ToString(Context.PathTable));

            return true;
        }

        /// <summary>
        /// The graph construction tool expects: path-to-repo-root path-to-output-graph path-to-npm commands-to-execute
        /// </summary>
        protected override string GetGraphConstructionToolArguments(AbsolutePath outputFile, AbsolutePath toolLocation, AbsolutePath bxlGraphConstructionToolPath, string nodeExeLocation)
        {
            // Node.exe sometimes misinterprets backslashes (e.g. "C:\" is interpreted such that \ is escaping quotes)
            // Use forward slashes for all node.exe arguments to avoid this.
            string pathToRepoRoot = ResolverSettings.Root.ToString(Context.PathTable, PathFormat.Script);

            // Get the list of all regular commands
            IEnumerable<string> commands = ComputedCommands.Keys.Where(command => !CommandGroups.ContainsKey(command)).Union(CommandGroups.Values.SelectMany(commandMembers => commandMembers)).ToList();

            using var sbWrapper = Pools.StringBuilderPool.GetInstance();
            var sb = sbWrapper.Instance;

            sb.Append($@"""{nodeExeLocation}""");
            sb.Append($@" ""{bxlGraphConstructionToolPath.ToString(Context.PathTable, PathFormat.Script)}""");
            sb.Append($@" ""{pathToRepoRoot}""");
            sb.Append($@" ""{outputFile.ToString(Context.PathTable, PathFormat.Script)}""");
            
            // The 4th argument is the npm location. If it is not specified, pass "undefined" string.
            // The graph construction tool actually will pick the 6th argument as tool to call over this one, but passing undefined here makes more explicit that the npm location
            // is not used
            if (m_useNpmLocation)
            {
                sb.Append($@" ""{toolLocation.ToString(Context.PathTable, PathFormat.Script)}""");
            }
            else
            {
                // Pass the 6th argument (lage location) as "undefined" string. This argument is used by Office implementation as well.
                sb.Append(@" ""undefined""");
            }

            sb.Append($@" ""{string.Join(" ", commands)}""");
            if (m_useNpmLocation)
            {
                // Pass the 6th argument (lage location) as "undefined" string. This argument is used by Office implementation.
                sb.Append(@" ""undefined""");
            }
            else
            {
                // The Lage location is explicitly specified, so pass it as the 6th argument
                sb.Append($@" ""{toolLocation.ToString(Context.PathTable, PathFormat.Script)}""");
            }
            
            _ = ResolverSettings.Since == null ? sb.Append(@" ""undefined""") : sb.Append($@" ""{ResolverSettings.Since}""");

            // The 8th argument is a boolean indicating whether to produce an error file. This can be removed after Office updates the direct consumption of the lage adapter
            // to pass 'false'. This is a temporary workaround to avoid breaking Office's build, since they run the adapter inside a pip, and the extra error file produces a DFA.
            sb.Append(" true");

            return JavaScriptUtilities.GetCmdArguments(sb.ToString());
        }

        /// <inheritdoc/>
        protected override string GetProjectNameForGroup(IReadOnlyCollection<JavaScriptProject> groupMembers, string groupCommandName)
        {
            Contract.Requires(groupMembers.Count > 0);
            var firstMember = groupMembers.First();

            // All members in the same group are supposed to share the same project name, so just use the first member
            var name = ExtractProjectName(firstMember.Name);

            // Let's keep the same Lage nomenclature for the group
            return $"{name}#{groupCommandName}";
        }

        /// <summary>
        /// Lage graph builder embeds the script command in the project name, so here we just remove it
        /// </summary>
        protected override string TryGetProjectDisplayName(string projectName, string scriptCommandName, AbsolutePath projectFolder) =>
            ExtractProjectName(projectName);

        /// <summary>
        /// Lage project names look like project-name#script-command
        /// </summary>
        private static string ExtractProjectName(string name)
        {
            var index = name.LastIndexOf('#');
            // There should always be a '#' in the name, but just be defensive here
            if (index >= 0)
            {
                name = name[..index];
            }

            return name;
        }
    }
}
