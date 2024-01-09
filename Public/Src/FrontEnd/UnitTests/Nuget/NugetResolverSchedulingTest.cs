// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast;
using Xunit;
using Xunit.Abstractions;
using System.Linq;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public class NugetResolverSchedulingTest : DsTestWithCacheBase
    {
        /// <summary>
        /// We just need the engine to schedule pips
        /// </summary>
        protected EnginePhases Phase => EnginePhases.Schedule;

        public NugetResolverSchedulingTest(ITestOutputHelper output) : base(output, usePassThroughFileSystem: true)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Core.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Script.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Nuget.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Nuget.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.FrontEnd.Sdk.ETWLogger.Log);
        }

        /// <summary>
        /// Test Esrp sign nuget resolver on schedule phase
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestSignNugetResolver()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // Set Nuget required Environment variables
                Environment.SetEnvironmentVariable("LOCALAPPDATA", SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));
                Environment.SetEnvironmentVariable("APPDATA", SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify));
                Environment.SetEnvironmentVariable("USERPROFILE", SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));

                // Create CommandlineConfiguration with a esrp sign enabled config.dsc
                var config = Build()
                    .Configuration(NugetResolverConfigurationWithEsrpSign())
                    .PersistSpecsAndGetConfiguration();

                // Set Engine on Schedule phase
                ((CommandLineConfiguration)config).Engine.Phase = Phase;

                var engineResult = CreateAndRunEngine(
                    config,
                    appDeployment,
                    testRootDirectory: null,
                    rememberAllChangedTrackedInputs: true,
                    engine: out var engine);

                XAssert.IsTrue(engineResult.IsSuccess);


                IReadOnlyList<Pip> processes = new List<Pip>(engineResult.EngineState.PipGraph.RetrievePipsOfType(PipType.Process));
                // Pip graph should have 2 Process pips, one is for downloading nuget package and the other one is for esrp signing
                Assert.Equal(2, processes.Count);
                // One Process pip has tool name 'NugetDownloader.exe'
                Assert.Equal("NugetDownloader.exe", ((Process)processes[0]).GetToolName(FrontEndContext.PathTable).ToString(FrontEndContext.StringTable));
                // The other Process pip has tool name 'invalid' since this test uses a invalid path for esrp sign
                Assert.Equal("invalid", ((Process)processes[1]).GetToolName(FrontEndContext.PathTable).ToString(FrontEndContext.StringTable));
            }
        }

        [Fact]
        public void TestFileExclusions()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                // Set Nuget required Environment variables
                Environment.SetEnvironmentVariable("LOCALAPPDATA", SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));
                Environment.SetEnvironmentVariable("APPDATA", SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify));
                Environment.SetEnvironmentVariable("USERPROFILE", SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));

                // Exclude a file that we know is part of ILRepack nuget package
                var excludeRelativePath = RelativePath.Create(FrontEndContext.StringTable, "tools/ILRepack.exe");

                var config = Build()
                    .Configuration(NugetResolverConfigurationWithFileExclusions($"r`{excludeRelativePath.ToString(FrontEndContext.StringTable)}`"))
                    .PersistSpecsAndGetConfiguration();

                ((CommandLineConfiguration)config).Engine.Phase = Phase;

                var engineResult = CreateAndRunEngine(
                    config,
                    appDeployment,
                    testRootDirectory: null,
                    rememberAllChangedTrackedInputs: true,
                    engine: out var engine);

                XAssert.IsTrue(engineResult.IsSuccess);

                // Pip graph should have 1 Process pips for downloading nuget package
                IReadOnlyList<Pip> processes = new List<Pip>(engineResult.EngineState.PipGraph.RetrievePipsOfType(PipType.Process));
                Assert.Equal(1, processes.Count);

                // The excluded file should be an untracked path
                var processPip = (Process)processes[0];
                var excludeAbsolutePath = processPip.WorkingDirectory.Combine(FrontEndContext.PathTable, "ILRepack.2.0.16").Combine(FrontEndContext.PathTable, excludeRelativePath);
                Assert.True(processPip.UntrackedPaths.Any(path => path.Equals(excludeAbsolutePath)));
            }
        }

        private string NugetResolverConfigurationWithEsrpSign()
        {
            return $@"
config({{
    resolvers: [
        {{
            kind: 'Nuget',
            repositories: {{
                  'packageFeed': '{NuSpecGeneratorTests.RetrieveTestPackageFeed()}',
            }},
            packages: [
                {{id: 'ILRepack', version: '2.0.16'}},
            ],
            esrpSignConfiguration: {{
                signToolPath: p`invalid`,
                signToolConfiguration: p`invalid`,
                signToolEsrpPolicy: p`invalid`,
                signToolAadAuth: p`invalid`,
            }},
            doNotEnforceDependencyVersions: true,
        }},
    ],
}});";
        }

        private string NugetResolverConfigurationWithFileExclusions(string exclusion)
        {
            return $@"
config({{
    resolvers: [
        {{
            kind: 'Nuget',
            repositories: {{
                'packageFeed': '{NuSpecGeneratorTests.RetrieveTestPackageFeed()}',
            }},
            packages: [
                {{id: 'ILRepack', version: '2.0.16', filesToExclude: [{exclusion}]}},
            ],
        }},
    ],
}});";
        }
    }
}
