// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if FEATURE_ANYBUILD_PROCESS_REMOTING

using System;
using System.IO;
using AnyBuild;
using BuildXL.Processes.Remoting;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public sealed class AnyBuildInstallerTests : XunitBuildXLTest
    {
        private const string MainFile = "AnyBuild.cmd";
        private const string VersionFile = "Current.txt";

        public AnyBuildInstallerTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestForceInstall()
        {
            TestShouldInstall(true, out string reason, forceInstall: true);
            XAssert.AreEqual("Force installation is enabled", reason);
        }

        [Fact]
        public void TestMissingInstallRoot()
        {
            string installRoot = TestShouldInstall(true, out string reason, existsInstallRoot: false);
            XAssert.AreEqual($"Installation directory '{installRoot}' not found", reason);
        }

        [Fact]
        public void TestMissingMainFile()
        {
            string installRoot = TestShouldInstall(true, out string reason, existsMainFile: false);
            XAssert.AreEqual($"AnyBuild main file '{MainFile}' in installation directory '{installRoot}' not found", reason);
        }

        [Fact]
        public void TestMissingVersionFile()
        {
            string installRoot = TestShouldInstall(true, out string reason, existsVersionFile: false);
            XAssert.AreEqual($"AnyBuild version file '{VersionFile}' in installation directory '{installRoot}' not found", reason);
        }

        [Fact]
        public void TestMissingVersion()
        {
            string installRoot = TestShouldInstall(
                true,
                out string reason,
                version: string.Empty,
                hook: root => File.WriteAllText(Path.Combine(root, VersionFile), string.Empty));
            XAssert.AreEqual($"AnyBuild version cannot be found in '{Path.Combine(installRoot, VersionFile)}'", reason);
        }

        [Theory]
        [InlineData("foo")]
        [InlineData("b36bd90f_20220319_151742")]
        [InlineData("b36bd90f_20220319")]
        public void TestMalformedVersion(string version)
        {
            string installRoot = TestShouldInstall(true, out string reason, version: version);
            XAssert.AreEqual($"Malformed AnyBuild version '{version}' found in '{Path.Combine(installRoot, VersionFile)}'", reason);
        }

        [Theory]
        [InlineData("b36bd90f_20220319.3_151742", "b36bd90f_20220319.3_151742", false)]
        [InlineData("b36bd90f_20220319.3_151742", "b36bddef_20220319.3_151742", false)]
        [InlineData("b36bd90f_20220320.3_151742", "b36bd90f_20220319.3_151742", false)]
        [InlineData("b36bd90f_20220318.3_151742", "b36bd90f_20220319.3_151742", true)]
        [InlineData("b36bd90f_20220319.2_151742", "b36bd90f_20220319.3_151742", true)]
        [InlineData("b36bd90f_20220319.4_151742", "b36bd90f_20220319.3_151742", false)]
        [InlineData("b36bd90f_20220319.3_151732", "b36bd90f_20220319.3_151742", true)]
        [InlineData("b36bd90f_20220319.3_151743", "b36bd90f_20220319.3_151742", false)]
        public void TestMeetMinimumRequirement(string version, string minRequiredVersion, bool shouldInstall)
        {
            TestShouldInstall(shouldInstall, out string reason, version: version, checkedVersion: minRequiredVersion);
            if (shouldInstall)
            {
                XAssert.AreEqual($"Current AnyBuild version '{version}' does not meet the minimum required version '{minRequiredVersion}'", reason);
            }
        }
        
        [Fact]
        public void TestMissingAbDir()
        {
            string currentVersion = "b36bd90f_22000319.3_151742"; // TODO: Our great great grandchildren will update this test.
            string installRoot = TestShouldInstall(true, out string reason, version: "b36bd90f_22000319.3_151742", existsVersionDir: false);
            XAssert.AreEqual($"AnyBuild directory for version '{currentVersion}' not found in '{installRoot}'", reason);
        }

        private static string TestShouldInstall(
            bool expectedInstall,
            out string reason,
            bool forceInstall = false,
            bool existsInstallRoot = true,
            bool existsMainFile = true,
            bool existsVersionFile = true,
            string version = null,
            string checkedVersion = null,
            bool existsVersionDir = true,
            Action<string> hook = null)
        {
            using var tempFiles = new TempFileStorage(canGetFileNames: true);
            
            string installRoot = existsInstallRoot ? tempFiles.GetUniqueDirectory() : null;
            PrepareInstallation(installRoot, existsMainFile, existsVersionFile, version, existsVersionDir);
            
            AnyBuildVersion minVersion = string.IsNullOrEmpty(checkedVersion) ? AnyBuildInstaller.MinRequiredVersion : AnyBuildVersion.Create(checkedVersion);
            XAssert.IsNotNull(minVersion);

            hook?.Invoke(installRoot);

            bool shouldInstall = AnyBuildInstaller.ShouldInstall(
                installRoot,
                MainFile,
                VersionFile,
                minVersion,
                forceInstall,
                out reason);
            XAssert.AreEqual(expectedInstall, shouldInstall);

            return installRoot;
        }

        private static void PrepareInstallation(string installRoot, bool existMainFile, bool existVersionFile, string version, bool existVersionDir)
        {
            if (string.IsNullOrEmpty(installRoot))
            {
                return;
            }

            Directory.CreateDirectory(installRoot);

            if (existMainFile)
            {
                string mainPath = Path.Combine(installRoot, MainFile);
                File.WriteAllText(mainPath, string.Empty);
            }

            if (existVersionFile)
            {
                string versionPath = Path.Combine(installRoot, VersionFile);
                if (!string.IsNullOrEmpty(version))
                {
                    File.WriteAllText(versionPath, Path.Combine(installRoot, version));
                }
            }

            if (existVersionDir && !string.IsNullOrEmpty(version))
            {
                Directory.CreateDirectory(Path.Combine(installRoot, version));
            }
        }
    }
}

#endif