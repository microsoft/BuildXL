// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Native.IO;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Executable
{
    /// <summary>
    /// Tests that call the dotnet core BuildXL executable directly.
    /// </summary>
    public class DotNetCoreExecutableTests : ExecutableTestBase
    {
        public DotNetCoreExecutableTests(ITestOutputHelper output) : base(output)
        {
            CreateLinkFromExampleModuleToBuildXLModule("Sdk.Prelude");
            CreateLinkFromExampleModuleToBuildXLModule("Sdk.Transformers");
        }

        private void CreateLinkFromExampleModuleToBuildXLModule(string moduleName)
        {
            var buildXlModule = Path.Combine(TestBxlDeploymentRoot, "Sdk", moduleName);
            var testBuildModule = Path.Combine(TestBuildRoot, "Sdk", moduleName);
            // If the symlink already exists for some reason, creating the symlink will fail, so just delete it
            FileUtilities.DeleteFile(testBuildModule);
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(testBuildModule, buildXlModule, isTargetFile: false).Succeeded);
        }

        [Fact]
        public void ExampleDotNetCoreBuild()
        {
            var args = $"/server- /cacheGraph- /remoteTelemetry+ /nowarn:0909,2840";
            var result = RunBuild(args).AssertSuccess();
        }
    }
}