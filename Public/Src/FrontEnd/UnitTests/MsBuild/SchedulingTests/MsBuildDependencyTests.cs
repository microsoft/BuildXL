// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.MsBuild.Serialization;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.MsBuild.Infrastructure;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Utilities.Configuration.Mutable;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    public sealed class MsBuildDependencyTests : MsBuildPipSchedulingTestBase
    {
        public MsBuildDependencyTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void DirectoryDependencyIsHonored()
        {
            var directoryOutput = TestPath.Combine(PathTable, "OutputDir");
            var projectB = CreateProjectWithPredictions("B.proj");
            var projectA = CreateProjectWithPredictions("A.proj", outputs: new[] { directoryOutput }, references: new[] { projectB });

            var result = Start()
                .Add(projectB)
                .Add(projectA)
                .ScheduleAll()
                .AssertSuccess();

            AssertDependencyAndDependent(projectB, projectA, result);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TransitiveDependencyIsHonoredUnderFlag(bool enableTransitiveProjectReferences)
        {
            // We create a graph A -> B -> C
            var projectC = CreateProjectWithPredictions("C.proj");
            var projectB = CreateProjectWithPredictions("B.proj", references: new[] { projectC });
            var projectA = CreateProjectWithPredictions("A.proj", references: new[] { projectB });

            var result = Start(new MsBuildResolverSettings() { EnableTransitiveProjectReferences = enableTransitiveProjectReferences })
                .Add(projectC)
                .Add(projectB)
                .Add(projectA)
                .ScheduleAll()
                .AssertSuccess();

            // We verify B -> C and A -> B
            AssertDependencyAndDependent(projectC, projectB, result);
            AssertDependencyAndDependent(projectB, projectA, result);

            // Transitive dependencies are enabled iff A -> C
            var projectADependsOnC = IsDependencyAndDependent(projectC, projectA, result);
            XAssert.AreEqual(enableTransitiveProjectReferences, projectADependsOnC);
        }

        #region Helpers

        private static void AssertDependencyAndDependent(ProjectWithPredictions<AbsolutePath> dependency, ProjectWithPredictions<AbsolutePath> dependent, MsBuildSchedulingResult result)
        {
            XAssert.IsTrue(IsDependencyAndDependent(dependency, dependent, result));
        }

        private static bool IsDependencyAndDependent(ProjectWithPredictions<AbsolutePath> dependency, ProjectWithPredictions<AbsolutePath> dependent, MsBuildSchedulingResult result)
        {
            // Unfortunately the test pip graph we are using doesn't keep track of dependencies/dependents. So we check there is a directory output of the dependency 
            // that is a directory input for a dependent
            var dependencyProcess = result.RetrieveSuccessfulProcess(dependency);
            var dependentProcess = result.RetrieveSuccessfulProcess(dependent);

            return dependencyProcess.DirectoryOutputs.Any(directoryOutput => dependentProcess.DirectoryDependencies.Any(directoryDependency => directoryDependency == directoryOutput));
        }

        #endregion
    }
}
