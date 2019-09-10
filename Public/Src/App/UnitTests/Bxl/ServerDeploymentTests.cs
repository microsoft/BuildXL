// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Reflection;
using BuildXL;
using BuildXL.Engine;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.Bxl
{
    public class ServerDeploymentTests : TemporaryStorageTestBase
    {
        [Fact]
        public void TestMissingManifestDirectory()
        {
            XAssert.IsTrue(ServerDeployment.IsServerDeploymentOutOfSync(TemporaryDirectory, null, out var deploymentDir));
        }

        [Fact]
        public void TestMissingManifestFile()
        {
            var manifestRootDir = TemporaryDirectory;
            var manifestPath = Path.Combine(manifestRootDir, AppDeployment.DeploymentManifestFileName);

            File.WriteAllText(manifestPath, AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));

            var appDeployment = AppDeployment.ReadDeploymentManifest(
                manifestRootDir,
                AppDeployment.DeploymentManifestFileName,
                skipManifestCheckTestHook: true);

            string deploymentDir = ServerDeployment.ComputeDeploymentDir(manifestRootDir);
            Directory.CreateDirectory(deploymentDir);

            XAssert.IsTrue(ServerDeployment.IsServerDeploymentOutOfSync(manifestRootDir, appDeployment, out deploymentDir));
        }

    }
}