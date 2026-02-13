// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Pips.Reclassification;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using System.Linq;

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Creates a pip based on a <see cref="JavaScriptProject"/> based on Rush
    /// </summary>
    internal sealed class RushPipConstructor : JavaScriptPipConstructor
    {
        private readonly RushConfiguration m_rushConfiguration;
        private readonly IRushResolverSettings m_resolverSettings;
        private readonly FrontEndContext m_context;

        /// <summary>
        /// Project-specific user profile folder
        /// </summary>
        internal static AbsolutePath UserProfile(JavaScriptProject project, PathTable pathTable) => project.TempFolder
            .Combine(pathTable, "USERPROFILE")
            .Combine(pathTable, PipConstructionUtilities.SanitizeStringForSymbol(project.ScriptCommandName));

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
            m_context = context;
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
            ProcessBuilder processBuilder,
            IReadOnlySet<JavaScriptProject> transitiveDependencies)
        {
            base.ProcessInputs(project, processBuilder, transitiveDependencies);
            
            // If dependencies should be tracked via the project-level shrinkwrap-deps file, then force an input
            // dependency on it
            if (m_resolverSettings.TrackDependenciesWithShrinkwrapDepsFile == true)
            {
                processBuilder.AddInputFile(FileArtifact.CreateSourceFile(project.ShrinkwrapDepsFile(PathTable)));
            }
        }

        /// <inheritdoc/>
        protected override void ProcessOutputs(
            JavaScriptProject project, 
            ProcessBuilder processBuilder, 
            IReadOnlySet<JavaScriptProject> transitiveDependencies)
        {
            base.ProcessOutputs(project, processBuilder, transitiveDependencies);
            
            // This makes sure the folder the user profile is pointing to gets actually created
            processBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(UserProfile(project, PathTable)), SealDirectoryKind.SharedOpaque);
        }

        /// <inheritdoc/>
        protected override bool TryConfigureProcessBuilder(
            ProcessBuilder processBuilder,
            JavaScriptProject project,
            IReadOnlySet<JavaScriptProject> transitiveDependencies)
        {
            if (!base.TryConfigureProcessBuilder(processBuilder, project, transitiveDependencies))
            {
                return false;
            }

            // If dependencies are tracked with the shrinkwrap-deps file, then untrack everything under the Rush common temp folder, where all package
            // dependencies are placed
            if (m_resolverSettings.TrackDependenciesWithShrinkwrapDepsFile == true)
            {
                processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(m_rushConfiguration.CommonTempFolder));
            }

            // The pnpm store is located under <common temp folder>/node_modules/.pnpm
            var pnpmStore = m_rushConfiguration.CommonTempFolder
                .Combine(PathTable, "node_modules")
                .Combine(PathTable, ".pnpm");

            // If pnpm store awareness tracking is enabled, add the corresponding reclassification rule
            if (m_resolverSettings.UsePnpmStoreAwarenessTracking == true)
            {
                var pnpmStoreRule = new JavaScriptPackageStoreReclassificationRule(m_resolverSettings.ModuleName, pnpmStore);

                if (!FileUtilities.DirectoryExistsNoFollow(pnpmStore.ToString(PathTable)))
                {
                    Tracing.Logger.Log.PnpmStoreNotFound(m_context.LoggingContext, m_resolverSettings.Location(PathTable), pnpmStore.ToString(m_context.PathTable));
                    return false;
                }

                processBuilder.ReclassificationRules = processBuilder.ReclassificationRules.Append(pnpmStoreRule).ToArray();
            }

            // Disallow writes under the pnpm store if explicitly specified, or if its value is left unspecified but pnpm store awareness tracking is on.
            // The rationale is that when pnpm store awareness is enabled, the assumption is that no writes happen under it during the build. Let's enforce that by excluding that scope
            // from the shared opaque umbrella, unless specified otherwise.
            if (m_resolverSettings.DisallowWritesUnderPnpmStore == true ||
                (m_resolverSettings.DisallowWritesUnderPnpmStore is null && m_resolverSettings.UsePnpmStoreAwarenessTracking == true))
            {
                processBuilder.AddOutputDirectoryExclusion(pnpmStore);
            }

            return true;
        }

        /// <inheritdoc/>
        protected override IEnumerable<AbsolutePath> GetResolverSpecificAllowedSourceReadsScopes()
        {
            var allowedScopes = base.GetResolverSpecificAllowedSourceReadsScopes();
            
            if (m_resolverSettings.RushLocation is null)
            {
                return allowedScopes;
            }

            return allowedScopes.Append(m_resolverSettings.RushLocation.Value.Path.GetParent(PathTable));
        }
    }
}
