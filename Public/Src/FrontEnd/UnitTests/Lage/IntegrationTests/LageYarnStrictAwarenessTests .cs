// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.JavaScript.Tracing;
using BuildXL.Native.IO;
using BuildXL.Scheduler;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Lage.IntegrationTests
{
    public class LageYarnStrictAwarenessTests : LageIntegrationTestBase
    {
        /// <summary>
        /// We need execution in order to test file accesses
        /// </summary>
        protected override EnginePhases Phase => EnginePhases.Execute;

        public LageYarnStrictAwarenessTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void YarnsStrictStoreNotPresentIsAnError()
        {
            var config = Build(useYarnStrictAwarenessTracking: true)
                .AddJavaScriptProject("@ms/project-A", "src/A", "module.exports = function A(){}")
                .PersistSpecsAndGetConfiguration();

            var result = RunLageProjects(config);
            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Lage.Tracing.LogEventId.YarnStrictStoreNotFound);
            AssertErrorEventLogged(LogEventId.ProjectGraphConstructionError);
            AssertErrorEventLogged(LogEventId.SchedulingPipFailure);
        }

        /// <summary>
        /// Nothing linux specific about this test, but on Windows node tries to write its compilation cache into a weird location and we get a DFA
        /// </summary>
        [FactIfSupported(requiresSymlinkPermission: true, requiresLinuxBasedOperatingSystem: true)]
        public void YarnsStrictAwarenessBehavior()
        {
            var config = Build(useYarnStrictAwarenessTracking: true)
                .AddJavaScriptProject("@ms/project-A", "src/A", "let a = require('mock-package'); a.mockPackage();")
                .AddFile(".store/mock-package@1.0.0/index.js", "function mockPackage(){}; module.exports = { mockPackage };")
                .PersistSpecsAndGetConfiguration();

            var result = RunLageProjects(config, postPackageInstallHook: () => 
                {
                    // We now need to simulate the symlinking process yarn strict does, so package A can find the package in the store
                    var packageRoot = config.Layout.SourceDirectory.Combine(PathTable, RelativePath.Create(StringTable, "src/A"));
                    var packageRootNodeModules = packageRoot.Combine(PathTable, "node_modules");
                    FileUtilities.CreateDirectory(packageRootNodeModules.ToString(PathTable));
                    var symlinkCreation = FileUtilities.TryCreateSymbolicLink(
                        packageRootNodeModules.Combine(PathTable, "mock-package").ToString(PathTable),
                        config.Layout.SourceDirectory.Combine(PathTable, RelativePath.Create(StringTable, ".store/mock-package@1.0.0")).ToString(PathTable),
                        isTargetFile: false);
                    Assert.True(symlinkCreation.Succeeded);
                });

            Assert.True(result.IsSuccess);
            var reclassifiedObservations = result.EngineState.SchedulerState.PipExecutionCounters[PipExecutorCounter.NumReclassifiedObservations];
            // There are 3 observations that get reclassified (based on node package resolution behavior):
            // .store/mock-package@1.0.0/index.js
            // .store/mock-package@1.0.0/package.json
            // .store/package.json
            // Plus a directory probe on .store/mock-package@1.0.0 when EBPF is on
            Assert.Equal(UsingEBPFSandbox? 4 : 3, reclassifiedObservations.Value);
        }
    }
}