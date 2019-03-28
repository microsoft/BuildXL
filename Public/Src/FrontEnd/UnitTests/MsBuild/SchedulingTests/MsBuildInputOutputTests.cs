// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.MsBuild.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Validates the scheduled pips for input/output declarations
    /// </summary>
    public sealed class MsBuildInputOutputSchedulingTests : MsBuildPipSchedulingTestBase
    {
        public MsBuildInputOutputSchedulingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void NoInputOutputFileIsProperlyScheduled()
        {
            Start()
                .Add(CreateProjectWithPredictions())
                .ScheduleAll()
                .AssertSuccess();
        }

        [Fact]
        public void InputsAreHonored()
        {
            var project = CreateProjectWithPredictions(inputs: CreatePath("input.txt"));

            var processInputs = Start()
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .Dependencies;

            // The corresponding scheduled process should have a declared input that matches the one in the project
            XAssert.IsTrue(processInputs.Any(file => file.Path.GetName(PathTable).ToString(PathTable.StringTable) == "input.txt"));
        }


        [Fact]
        public void OutputDirectoriesAreCoveredBySharedOpaque()
        {
            var project = CreateProjectWithPredictions(outputs: CreatePath("outputDir"));

            var processOutputDirectories = Start()
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .DirectoryOutputs;

            // There needs to be a shared opaque covering the intermediate output dir
            XAssert.IsTrue(processOutputDirectories.Any(outputDirectory => outputDirectory.IsSharedOpaque && project.PredictedOutputFolders.Single().IsWithin(PathTable, outputDirectory.Path)));
        }

        [Fact]
        public void CatchAllSharedOpaqueIsCreated()
        {
            // An empty project, with no outputs
            var project = CreateProjectWithPredictions();

            var processOutputDirectories = Start()
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .DirectoryOutputs;

            // A single shared opaque at the root should be created
            var catchAllSharedOpaque = processOutputDirectories.Single();
            XAssert.IsTrue(catchAllSharedOpaque.IsSharedOpaque);
            XAssert.AreEqual(TestPath, catchAllSharedOpaque.Path);
        }

        [Fact]
        public void SharedOpaquesOutsideCatchAllIsCreatedAsNeeded()
        {
            var outOfRootOutput = TestPath.GetParent(PathTable).Combine(PathTable, "outOfRoot.txt");
            var project = CreateProjectWithPredictions(outputs: new[] { outOfRootOutput });

            var processOutputDirectories = Start()
                .Add(project)
                .ScheduleAll()
                .RetrieveSuccessfulProcess(project)
                .DirectoryOutputs;

            // There needs to be a shared opaque covering the intermediate output dir
            XAssert.IsTrue(processOutputDirectories.Any(outputDirectory => outputDirectory.IsSharedOpaque && outOfRootOutput.IsWithin(PathTable, outputDirectory.Path)));
        }

        private IReadOnlyCollection<AbsolutePath> CreatePath(params string[] paths)
        {
            return paths.Select(path => TestPath.Combine(PathTable, path)).ToList();
        }
    }
}
