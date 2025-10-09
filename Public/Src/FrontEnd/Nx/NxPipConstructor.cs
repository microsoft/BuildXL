// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Nx.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Interop.Unix;
using BuildXL.Pips.Builders;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Nx
{
    /// <summary>
    /// Creates a pip based on a <see cref="JavaScriptProject"/> based on Nx
    /// </summary>
    internal sealed class NxPipConstructor : JavaScriptPipConstructor
    {
        private INxResolverSettings NxResolverSettings => (INxResolverSettings)ResolverSettings;

        private AbsolutePath GetSocketDir() => AbsolutePath.Create(PathTable, Path.GetTempPath());

        /// <nodoc/>
        public NxPipConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            NxConfiguration nxConfiguration,
            INxResolverSettings resolverSettings,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables,
            IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> customCommands,
            IEnumerable<JavaScriptProject> allProjectsToBuild)
        : base(context, frontEndHost, moduleDefinition, resolverSettings, userDefinedEnvironment, userDefinedPassthroughVariables, customCommands, allProjectsToBuild)
        {
        }

        /// <inheritdoc/>
        protected override Dictionary<string, string> DoCreateEnvironment(JavaScriptProject project)
        {
            var env = base.DoCreateEnvironment(project);

            // Disable the Nx daemon. This minimizes the chance of Nx having lingering processes that may try to escape from the sandbox.
            env.TryAdd(NxFrontEnd.NxDaemonOffEnvVar.key, NxFrontEnd.NxDaemonOffEnvVar.value);
            // Disable the Nx DB. Nx is not really prepared to be called concurrently as a pure executor, and writes to the nx internal DB in a racy way (and not really needed for our purposes).
            env.TryAdd(NxFrontEnd.NxDBOffEnvVar.key, NxFrontEnd.NxDBOffEnvVar.value);
            // Point to a shorter temp folder for the socket dir, to avoid issues with max socket length
            env.TryAdd(NxFrontEnd.NxSocketDirVar, GetSocketDir().ToString(PathTable));

            return env;
        }

        /// <inheritdoc/>
        protected override bool TryConfigureProcessBuilder(ProcessBuilder processBuilder, JavaScriptProject project, IReadOnlySet<JavaScriptProject> transitiveDependencies)
        {
            if (!base.TryConfigureProcessBuilder(processBuilder, project, transitiveDependencies))
            {
                return false;
            }

            // By using nx as the main executor for each pip, there is some state that nx tries to keep in a folder named .nx at the root of the workspace.
            // Make sure that this folder is not tracked by BuildXL, there are some reads and writes happening there we should ignore.
            processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(NxResolverSettings.GetNxInternalFolder(PathTable)));

            // By default pips get the project root as their working directory. That's not the case for Nx, where the working directory is the workspace root.
            processBuilder.WorkingDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(ResolverSettings.Root);

            // The socket dir also needs to be untracked, since Nx will try to create and delete sockets there.
            processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(GetSocketDir()));

            return true;
        }
    }
}
