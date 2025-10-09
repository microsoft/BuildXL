// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Nx.ProjectGraph;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Processes;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Nx
{
    /// <summary>
    /// Workspace resolver for Nx
    /// </summary>
    public class NxWorkspaceResolver : ToolBasedJavaScriptWorkspaceResolver<NxConfiguration, NxResolverSettings>
    {
        /// <summary>
        /// CODESYNC: the BuildXL deployment spec that places the tool
        /// </summary>
        protected override RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(Context.StringTable, @"tools\NxGraphBuilder\main.js");

        /// <summary>
        /// Nx provides its own execution semantics
        /// </summary>
        protected override bool ApplyBxlExecutionSemantics() => false;

        /// <inheritdoc/>
        public NxWorkspaceResolver() : base(KnownResolverKind.NxResolverKind)
        {
        }

        /// <inheritdoc/>
        protected override bool TryFindGraphBuilderToolLocation(NxResolverSettings resolverSettings, BuildParameters.IBuildParameters buildParameters, out AbsolutePath toolLocation, out string failure)
        {
            // If the nx location was provided at configuration time, we honor it as is
            if (resolverSettings.NxLibLocation.IsValid)
            {
               toolLocation = resolverSettings.NxLibLocation.Path;
               failure = string.Empty;
               return true;
            }

            // When the nx lib location is not explicitly provided, we try to find it based on where nx is located. This is very likely to change, for now we do a very naive search
            // Let's try to find nx in PATH.
            string paths = buildParameters["PATH"];

            toolLocation = AbsolutePath.Invalid;
            if (!FrontEndUtilities.TryFindToolInPath(Context, Host, paths, new[] { "nx", "nx.exe", "nx.cmd" }, out var nxBinLocation))
            {
                failure = "A location for 'nx' is not explicitly specified. However, 'nx' doesn't seem to be part of PATH. You can either specify the location explicitly using 'nxLocation' field in " +
                    $"the Nx resolver configuration, or make sure 'nx' is part of your PATH. Current PATH is '{paths}'.";

                return false;
            }

            // We found where nx is located, but we need where the nx libraries are located. Let's try a couple of options
            // First try looking for node_modules in the parent folder
            // TODO: this list is likely incomplete. Unclear whether there is a better way to discover where nx is installed. But overall, this is not critical, the main use case is where the user specifies the location explicitly
            var pathsToTry = new List<AbsolutePath>()
            {
                // Try {repoRoot}/node_modules/nx
                resolverSettings.Root.Combine(Context.PathTable, "node_modules").Combine(Context.PathTable, "nx"),
                // Try ../node_modules/nx
                nxBinLocation.GetParent(Context.PathTable).Combine(Context.PathTable, "node_modules").Combine(Context.PathTable, "nx"),
                // Try ../../lib/node_modules/nx
                nxBinLocation.GetParent(Context.PathTable).GetParent(Context.PathTable).Combine(Context.PathTable, "lib").Combine(Context.PathTable, "node_modules").Combine(Context.PathTable, "nx"),
                // Try /usr/lib/nx/node_modules/nx on Unix systems
                OperatingSystemHelper.IsUnixOS ? AbsolutePath.Create(Context.PathTable, "/usr/lib/nx/node_modules") : AbsolutePath.Invalid
            };

            foreach (var path in pathsToTry.Where(p => p.IsValid))
            {
                if (Directory.Exists(path.ToString(Context.PathTable)))
                {
                    toolLocation = path;
                    break;
                }
            }

            if (toolLocation.IsValid)
            {
                failure = string.Empty;
                // Just verbose log this
                Tracing.Logger.Log.UsingToolAt(Context.LoggingContext, resolverSettings.Location(Context.PathTable), toolLocation.ToString(Context.PathTable));

                return true;                
            }

            failure = $"nx was found under {nxBinLocation.ToString(Context.PathTable)}, but the libraries were not found under the following paths: " +
                $"{string.Join(", ", pathsToTry.Where(p => p.IsValid).Select(p => p.ToString(Context.PathTable))) }.";
            toolLocation = AbsolutePath.Invalid;

            return false;
        }

        /// <inheritdoc/>
        protected override BuildParameters.IBuildParameters RetrieveBuildParameters()
        {
            var parameters = base.RetrieveBuildParameters();

            // Disable the Nx daemon for graph construction. This minimizes the chance of Nx having lingering processes that may try to escape from the sandbox.   
            var daemonOff = new KeyValuePair<string, string>(NxFrontEnd.NxDaemonOffEnvVar.key, NxFrontEnd.NxDaemonOffEnvVar.value);
            return parameters.Override(new[] { daemonOff });
        }

        /// <nodoc/>
        protected override FileAccessManifest GenerateFileAccessManifest(AbsolutePath toolDirectory)
        {
            var manifest = base.GenerateFileAccessManifest(toolDirectory);

            // Let's ignore all the accesses under the .nx folder. Nx reads and writes state during graph construction that we won't want to track, since otherwise
            // it will be always a cache miss. The content of the folder is assumed to be inconsequential to the result of the graph construction.
            manifest.AddScope(
                ResolverSettings.GetNxInternalFolder(Context.PathTable),
                mask: ~FileAccessPolicy.ReportAccess,
                values: FileAccessPolicy.AllowAll | FileAccessPolicy.AllowRealInputTimestamps);

            return manifest;
        }

        /// <summary>
        /// The graph construction tool expects: path-to-repo-root path-to-output-graph path-to-nx-libs path-to-node targets
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
            sb.Append($@" ""{toolLocation.ToString(Context.PathTable, PathFormat.Script)}""");
            sb.Append($@" ""{nodeExeLocation}""");
            sb.Append($@" ""{string.Join(",", ComputedCommands.Keys)}""");

            return JavaScriptUtilities.GetCmdArguments(sb.ToString());
        }

        /// <inheritdoc/>
        protected override string GetProjectNameForGroup(IReadOnlyCollection<JavaScriptProject> groupMembers, string groupCommandName)
        {
            Contract.Requires(groupMembers.Count > 0);
            var firstMember = groupMembers.First();

            // All members in the same group are supposed to share the same project name, so just use the first member
            var name = ExtractProjectName(firstMember.Name);

            // Let's keep the same Nx nomenclature for the group
            return $"{name}:{groupCommandName}";
        }

        /// <summary>
        /// Nx graph builder embeds the script command in the project name, so here we just remove it
        /// </summary>
        protected override string TryGetProjectDisplayName(string projectName, string scriptCommandName, AbsolutePath projectFolder) =>
            ExtractProjectName(projectName);

        /// <summary>
        /// Nx project names look like project-name:script-command
        /// </summary>
        private static string ExtractProjectName(string name)
        {
            var index = name.LastIndexOf(':');
            // There should always be a ':' in the name, but just be defensive here
            if (index >= 0)
            {
                name = name[..index];
            }

            return name;
        }
    }
}
