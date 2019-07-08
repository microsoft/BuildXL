// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Engine.Tracing;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using Test.BuildXL.EngineTestUtilities;
using Xunit;
using Xunit.Abstractions;
using System;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Uses an MSBuild resolver to schedule and execute pips based on MSBuild.
    /// </summary>
    /// <remarks>
    /// These tests actually execute pips, and are therefore expensive
    /// </remarks>
    public class MsBuildDotNetRuntimeTests : MsBuildPipExecutionTestBase
    {
        public MsBuildDotNetRuntimeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("DotNetCore")]
        [InlineData("FullFramework")]
        public void RuntimeSelectionIsEffective(string msBuildRuntime)
        {
            const string TestProj1 = "test1.csproj";
            var pathToTestProj1 = R("public", "dir1", TestProj1);
            
            const string Dirs = "dirs.proj";
            var config = (CommandLineConfiguration)Build(
                        msBuildRuntime: msBuildRuntime, 
                        dotnetSearchLocations: $"[d`{TestDeploymentDir}/{RelativePathToDotnetExe}`]")
                    .AddSpec(Dirs, CreateDirsProject(pathToTestProj1))
                    .AddSpec(pathToTestProj1, CreateEmptyProject())
                    .PersistSpecsAndGetConfiguration();

            config.Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;

            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);

            var pipGraph = engineResult.EngineState.PipGraph;

            var processPips = pipGraph.RetrievePipsOfType(PipType.Process).ToList();
            var testProj1 = (Process)processPips.Find(pip => pip.Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains(TestProj1));
            Assert.True(testProj1 != null);

            if (msBuildRuntime == "DotNetCore")
            {
                // The main executable has to be dotnet.exe (or dotnet in the mac/unix case)
                Assert.Contains("DOTNET", testProj1.Executable.Path.ToString(PathTable).ToUpperInvariant());
            }
            else
            {
                // The main executable has to be msbuild.exe
                Assert.Contains("MSBUILD.EXE", testProj1.Executable.Path.ToString(PathTable).ToUpperInvariant());
            }
        }
    }
}
