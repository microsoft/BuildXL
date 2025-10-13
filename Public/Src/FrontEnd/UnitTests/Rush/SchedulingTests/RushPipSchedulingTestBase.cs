// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Rush;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast.Scheduling;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Base class for tests that programmatically add projects and verify the corresponding scheduled process
    /// done by <see cref="RushResolver"/>
    /// </summary>
    public abstract class RushPipSchedulingTestBase : PipSchedulingTestBase<JavaScriptProject, RushResolverSettings>
    {
        private AbsolutePath m_commonTempFolder = AbsolutePath.Invalid;

        /// <nodoc/>
        public RushPipSchedulingTestBase(ITestOutputHelper output, bool usePassThroughFileSystem = false) : base(output, usePassThroughFileSystem)
        {
        }

        /// <summary>
        /// Starts the addition of projects using a given Rush common temp folder
        /// </summary>
        public ProjectBuilder<JavaScriptProject, RushResolverSettings> StartWithCommonTempFolder(
            AbsolutePath commonTempFolder,
            RushResolverSettings resolverSettings = null,
            QualifierId currentQualifier = default,
            QualifierId[] requestedQualifiers = default,
            SandboxConfiguration sandboxConfiguration = null)
        {
            m_commonTempFolder = commonTempFolder;
            return Start(resolverSettings, currentQualifier, requestedQualifiers, sandboxConfiguration);
        }

        /// <summary>
        /// Starts the addition of projects
        /// </summary>
        public override ProjectBuilder<JavaScriptProject, RushResolverSettings> Start(
            RushResolverSettings resolverSettings = null, 
            QualifierId currentQualifier = default, 
            QualifierId[] requestedQualifiers = default,
            SandboxConfiguration sandboxConfiguration = null)
        {
            var settings = resolverSettings ?? new RushResolverSettings();
            
            // Make sure the Root is set
            if (settings.Root == AbsolutePath.Invalid)
            {
                settings.Root = AbsolutePath.Create(PathTable, TestRoot);
            }

            // If the common temp folder is not set explicitly, use <sourceRoot>/common/temp, which is usually the Rush default
            if (!m_commonTempFolder.IsValid)
            {
                m_commonTempFolder = settings.Root.Combine(PathTable, RelativePath.Create(StringTable, "common/temp"));
            }

            return base.Start(settings, currentQualifier, requestedQualifiers, sandboxConfiguration);
        }

        /// <summary>
        /// Helper method to create a rush project 
        /// </summary>
        public JavaScriptProject CreateRushProject(
            string projectName = null, 
            string scriptCommandName = null,
            string scriptCommand = null,
            AbsolutePath? tempFolder = null,
            IReadOnlyCollection<AbsolutePath> outputDirectories = null,
            IReadOnlyCollection<FileArtifact> inputFiles = null,
            IReadOnlyCollection<JavaScriptProject> dependencies = null,
            IReadOnlyCollection<DirectoryArtifact> inputDirectories = null,
            AbsolutePath? projectFolder = null)
        {
            projectName ??= "@ms/rush-proj";

            var tempDirectory = tempFolder.HasValue ? tempFolder.Value : AbsolutePath.Create(PathTable, GetTempDir());
            var rushProject = new JavaScriptProject(
                projectName,
                projectFolder ?? TestPath.Combine(PathTable, RelativePath.Create(StringTable, projectName)),
                scriptCommandName ?? "build",
                scriptCommand ?? "node ./main.js",
                tempDirectory,
                outputDirectories ?? CollectionUtilities.EmptyArray<AbsolutePath>(),
                inputFiles ?? CollectionUtilities.EmptyArray<FileArtifact>(),
                inputDirectories ?? CollectionUtilities.EmptyArray<DirectoryArtifact>(),
                sourceDirectories: CollectionUtilities.EmptyArray<AbsolutePath>(),
                cacheable: true,
                tags: System.Array.Empty<string>()
            );

            rushProject.SetDependencies(dependencies ?? CollectionUtilities.EmptyArray<JavaScriptProject>());

            return rushProject;
        }

        protected override IProjectToPipConstructor<JavaScriptProject> CreateProjectToPipConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            RushResolverSettings resolverSettings,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables,
            IEnumerable<JavaScriptProject> allProjects)
        {
            return new RushPipConstructor(
                context,
                frontEndHost,
                moduleDefinition,
                new RushConfiguration(m_commonTempFolder),
                resolverSettings,
                userDefinedEnvironment,
                userDefinedPassthroughVariables,
                CollectionUtilities.EmptyDictionary<string, IReadOnlyList<JavaScriptArgument>>(),
                allProjects);
        }
    }
}
