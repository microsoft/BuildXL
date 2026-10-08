// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.EngineTests
{
    public class AppDeploymentTests : TemporaryStorageTestBase
    {
        private const string TestServerManifestName = "test.serverdeployment.manifest";

        [Fact]
        public void Test()
        {
            const string SpmiDataFileName = "Security.SharedStrings.Internal.txt";
            string root = Path.Combine(TemporaryDirectory, "testDeployment");
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, TestServerManifestName),
@"included1.dll
included1.pdb
excluded1.txt
subdir" + Path.DirectorySeparatorChar + "included2.exe" + Environment.NewLine +
SpmiDataFileName + Environment.NewLine +
AppDeployment.BuildXLBrandingManifestFileName);

            File.WriteAllText(Path.Combine(root, "included1.dll"), "test");
            // Include pdbs to aid with call stacks when debugging
            File.WriteAllText(Path.Combine(root, "included1.pdb"), "test");
            File.WriteAllText(Path.Combine(root, "excluded1.txt"), "test");
            File.WriteAllText(Path.Combine(root, "excluded2.dll"), "test");
            File.WriteAllText(Path.Combine(root, SpmiDataFileName), "test");
            File.WriteAllText(Path.Combine(root, AppDeployment.BuildXLBrandingManifestFileName), "test");
            Directory.CreateDirectory(Path.Combine(root, "subdir"));
            File.WriteAllText(Path.Combine(root, "subdir", "included2.exe"), "test");

            // Create an initial app deployment and verify it is correct
            AppDeployment deployment = AppDeployment.ReadDeploymentManifest(root, TestServerManifestName);
            // PDB files should only be included for the server deployment.
            XAssert.AreEqual(5, deployment.GetRelevantRelativePaths(forServerDeployment: true).Count());
            XAssert.AreEqual(4, deployment.GetRelevantRelativePaths(forServerDeployment: false).Count());
            Assert.Contains(SpmiDataFileName, deployment.GetRelevantRelativePaths(forServerDeployment: true));
            Assert.Contains(SpmiDataFileName, deployment.GetRelevantRelativePaths(forServerDeployment: false));
            Fingerprint originalContentHash = ComputeContentHash(deployment);

            // Now mess with files that should not impact the state and make sure they are excluded

            // This file is excluded because of the extension. It is in the manifest
            UpdateFile(Path.Combine(root, "excluded1.txt"));

            // This file is excluded because it isn't in the deployment manifest
            UpdateFile(Path.Combine(root, "excluded2.dll"));

            AppDeployment deployment2 = AppDeployment.ReadDeploymentManifest(root, TestServerManifestName);
            XAssert.AreEqual(
                deployment.TimestampBasedHash.ToHex(),
                deployment2.TimestampBasedHash.ToHex());
            XAssert.AreEqual(
                originalContentHash,
                ComputeContentHash(deployment2));

            // Mess with a file that is in the deployment and check that the hash does change
            UpdateFile(Path.Combine(root, "subdir", "included2.exe"));
            AppDeployment deployment3 = AppDeployment.ReadDeploymentManifest(root, TestServerManifestName);
            XAssert.AreNotEqual(
                deployment.TimestampBasedHash.ToHex(),
                deployment3.TimestampBasedHash.ToHex());
            Fingerprint changedBinaryContentHash = ComputeContentHash(deployment3);
            XAssert.AreNotEqual(originalContentHash, changedBinaryContentHash);

            UpdateFile(Path.Combine(root, SpmiDataFileName));
            AppDeployment changedDeployment = AppDeployment.ReadDeploymentManifest(root, TestServerManifestName);
            XAssert.AreNotEqual(deployment3.TimestampBasedHash, changedDeployment.TimestampBasedHash);
            XAssert.AreNotEqual(changedBinaryContentHash, ComputeContentHash(changedDeployment));
        }

        /// <summary>
        /// Validates that BuildXL fails if the identification manifest file is missing from disk or from the server deployment manifest
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IdentityManifestMissing(bool includeInDeploymentManifest)
        {
            string root = Path.Combine(TemporaryDirectory, "testDeployment");
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, TestServerManifestName),
                "included1.dll" + Environment.NewLine +
                (includeInDeploymentManifest ? AppDeployment.BuildXLBrandingManifestFileName : string.Empty));

            if (!includeInDeploymentManifest)
            {
                File.WriteAllText(Path.Combine(root, AppDeployment.BuildXLBrandingManifestFileName), "whatever");
            }

            Assert.Throws<BuildXLException>(() => AppDeployment.ReadDeploymentManifest(root, TestServerManifestName));
        }

        private Fingerprint ComputeContentHash(AppDeployment deployment)
        {
            // Recording file versions can adjust modification times on Unix.
            return deployment.ComputeContentHashBasedFingerprint(FileContentTable.CreateStub(CreateLoggingContextForTest()));
        }

        /// <summary>
        /// Updates a file by adding some content and incrementing its last write timestamp
        /// </summary>
        private void UpdateFile(string path)
        {
            FileInfo fi = new FileInfo(path);

            using (StreamWriter writer = fi.AppendText())
            {
                writer.Write("update");
            }

            fi.LastWriteTimeUtc = fi.LastWriteTimeUtc.AddMinutes(5);
        }
    }
}
