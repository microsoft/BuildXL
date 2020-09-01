// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Creates a pip based on a <see cref="JavaScriptProject"/> based on Rush
    /// </summary>
    internal sealed class RushPipConstructor : JavaScriptPipConstructor
    {
        private readonly RushConfiguration m_rushConfiguration;
        private readonly IRushResolverSettings m_resolverSettings;

        /// <summary>
        /// Project-specific user profile folder
        /// </summary>
        internal static AbsolutePath UserProfile(JavaScriptProject project, PathTable pathTable) => project.TempFolder
            .Combine(pathTable, "USERPROFILE")
            .Combine(pathTable, project.ScriptCommandName);

        /// <nodoc/>
        public RushPipConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            RushConfiguration rushConfiguration,
            IRushResolverSettings resolverSettings,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables,
            IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> customCommands,
            IEnumerable<JavaScriptProject> allProjectsToBuild) 
        : base(context, frontEndHost, moduleDefinition, resolverSettings, userDefinedEnvironment, userDefinedPassthroughVariables, customCommands, allProjectsToBuild)
        {
            Contract.RequiresNotNull(rushConfiguration);

            m_rushConfiguration = rushConfiguration;
            m_resolverSettings = resolverSettings;
        }

        protected override Dictionary<string, string> DoCreateEnvironment(JavaScriptProject project)
        {
            var env = base.DoCreateEnvironment(project);
            
            // redirect the user profile so it points under the temp folder
            // use a different path for each build command, since there are tools that happen to generate the same file for, let's say, build and test
            // and we want to avoid double writes as much as possible
            env["USERPROFILE"] = UserProfile(project, PathTable).ToString(PathTable);
            
            return env;
        }

        /// <inheritdoc/>
        protected override void ProcessInputs(
            JavaScriptProject project,
            ProcessBuilder processBuilder)
        {
            base.ProcessInputs(project, processBuilder);
            
            // If dependencies should be tracked via the project-level shrinkwrap-deps file, then force an input
            // dependency on it
            if (m_resolverSettings.TrackDependenciesWithShrinkwrapDepsFile == true)
            {
                processBuilder.AddInputFile(FileArtifact.CreateSourceFile(project.ShrinkwrapDepsFile(PathTable)));
            }
        }

        /// <inheritdoc/>
        protected override void ProcessOutputs(JavaScriptProject project, ProcessBuilder processBuilder)
        {
            base.ProcessOutputs(project, processBuilder);
            
            // This makes sure the folder the user profile is pointing to gets actually created
            processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(UserProfile(project, PathTable)), SealDirectoryKind.SharedOpaque);
        }

        /// <inheritdoc/>
        protected override void ConfigureProcessBuilder(
            ProcessBuilder processBuilder,
            JavaScriptProject project)
        {
            base.ConfigureProcessBuilder(processBuilder, project);

            // If dependencies are tracked with the shrinkwrap-deps file, then untrack everything under the Rush common temp folder, where all package
            // dependencies are placed
            if (m_resolverSettings.TrackDependenciesWithShrinkwrapDepsFile == true)
            {
                processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(m_rushConfiguration.CommonTempFolder));
            }
        }
    }
}
