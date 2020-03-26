// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.Rush;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
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
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public abstract class RushPipSchedulingTestBase : PipSchedulingTestBase<RushProject, RushResolverSettings>
    {
        /// <nodoc/>
        public RushPipSchedulingTestBase(ITestOutputHelper output, bool usePassThroughFileSystem = false) : base(output, usePassThroughFileSystem)
        {
        }

        /// <summary>
        /// Starts the addition of projects
        /// </summary>
        /// <returns></returns>
        public override ProjectBuilder<RushProject, RushResolverSettings> Start(
            RushResolverSettings resolverSettings = null, 
            QualifierId currentQualifier = default, 
            QualifierId[] requestedQualifiers = default)
        {
            var settings = resolverSettings ?? new RushResolverSettings();
            
            // Make sure the Root is set
            if (settings.Root == AbsolutePath.Invalid)
            {
                settings.Root = AbsolutePath.Create(PathTable, TestRoot);
            }

            return base.Start(settings, currentQualifier, requestedQualifiers);
        }

        /// <summary>
        /// Helper method to create a rush project 
        /// </summary>
        public RushProject CreateRushProject(
            string projectName = null, 
            string buildCommand = null,
            AbsolutePath? tempFolder = null,
            IReadOnlyCollection<AbsolutePath> additionalOutputDirectories = null,
            IReadOnlyCollection<RushProject> dependencies = null)
        {
            projectName ??= "@ms/rush-proj";

            var tempDirectory = tempFolder.HasValue ? tempFolder.Value : AbsolutePath.Create(PathTable, GetTempDir());
            var rushProject = new RushProject(
                projectName,
                TestPath.Combine(PathTable, RelativePath.Create(StringTable, projectName)),
                buildCommand ?? "node ./main.js",
                tempDirectory,
                additionalOutputDirectories ?? CollectionUtilities.EmptyArray<AbsolutePath>()
            );

            rushProject.SetDependencies(dependencies ?? CollectionUtilities.EmptyArray<RushProject>());

            return rushProject;
        }

        protected override IProjectToPipConstructor<RushProject> CreateProjectToPipConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            RushResolverSettings resolverSettings,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables)
        {
            return new RushPipConstructor(context, frontEndHost, moduleDefinition, resolverSettings, userDefinedEnvironment, userDefinedPassthroughVariables);
        }
    }
}
