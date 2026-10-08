// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Reflection;
using BuildXL;
using BuildXL.Engine;
using BuildXL.Utilities.Core;
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

        /// <summary>
        /// Verifies that an eligible manifested file is copied, unchanged deployments are reused,
        /// and source changes recreate the server cache with updated contents.
        /// </summary>
        [Fact]
        public void ManifestedFileChangesInvalidateServerCache()
        {
            const string IncludedFileName = "included.dll";
            string clientDir = Path.Combine(TemporaryDirectory, "client");
            string serverRoot = Path.Combine(TemporaryDirectory, "server");
            Directory.CreateDirectory(clientDir);
            string sourcePath = Path.Combine(clientDir, IncludedFileName);
            File.WriteAllText(sourcePath, "test");
            File.WriteAllText(Path.Combine(clientDir, "excluded1.txt"), "test");
            File.WriteAllText(Path.Combine(clientDir, AppDeployment.BuildXLBrandingManifestFileName), "test");
            File.WriteAllLines(
                Path.Combine(clientDir, AppDeployment.ServerDeploymentManifestFileName),
                new[] { IncludedFileName, "excluded1.txt", AppDeployment.BuildXLBrandingManifestFileName });

            AppDeployment clientDeployment = AppDeployment.ReadDeploymentManifest(clientDir, AppDeployment.ServerDeploymentManifestFileName);
            ServerDeployment serverDeployment = ServerDeployment.GetOrCreateServerDeploymentCache(serverRoot, clientDeployment);
            XAssert.IsTrue(serverDeployment.CacheCreationInformation.HasValue);
            XAssert.AreEqual("test", File.ReadAllText(Path.Combine(serverDeployment.DeploymentPath, IncludedFileName)));
            XAssert.IsFalse(File.Exists(Path.Combine(serverDeployment.DeploymentPath, "excluded1.txt")));
            XAssert.IsFalse(ServerDeployment.IsServerDeploymentOutOfSync(serverRoot, clientDeployment, out _));
            XAssert.IsFalse(ServerDeployment.GetOrCreateServerDeploymentCache(serverRoot, clientDeployment).CacheCreationInformation.HasValue);

            File.WriteAllText(sourcePath, "updated data");
            File.SetLastWriteTimeUtc(sourcePath, File.GetLastWriteTimeUtc(sourcePath).AddMinutes(5));
            clientDeployment = AppDeployment.ReadDeploymentManifest(clientDir, AppDeployment.ServerDeploymentManifestFileName);
            XAssert.IsTrue(ServerDeployment.IsServerDeploymentOutOfSync(serverRoot, clientDeployment, out _));

            serverDeployment = ServerDeployment.GetOrCreateServerDeploymentCache(serverRoot, clientDeployment);
            XAssert.IsTrue(serverDeployment.CacheCreationInformation.HasValue);
            XAssert.AreEqual("updated data", File.ReadAllText(Path.Combine(serverDeployment.DeploymentPath, IncludedFileName)));
            XAssert.IsFalse(ServerDeployment.IsServerDeploymentOutOfSync(serverRoot, clientDeployment, out _));
        }

    }
}