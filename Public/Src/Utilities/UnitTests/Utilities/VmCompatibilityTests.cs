// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.VmCommandProxy;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// This test class checks if the assumptions about VM execution in BuildXL are valid.
    /// </summary>
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true, requiresAdmin: true)]
    public class VmCompatibilityTests
    {
        [Fact]
        public void VmRunWithAdminAccountTest()
        {
            EnsureRunInVm(() =>
            {
                XAssert.AreEqual(VmConstants.UserProfile.Name, Environment.UserName);
            });
        }

        [Fact]
        public void VmRunHasAdminUserProfilePath()
        {
            EnsureRunInVm(() =>
            {
                XAssert.AreEqual(
                    VmConstants.UserProfile.Path.ToUpperInvariant(),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).ToUpperInvariant());
            });
        }

        /// <summary>
        /// Test that the pip that runs in VM can see the host's user profile directory.
        /// </summary>
        [Fact]
        public void VmCanSeeHostUserProfileDirectory()
        {
            EnsureRunInVm(() =>
            {
                string hostUserProfilePath = Environment.GetEnvironmentVariable(VmSpecialEnvironmentVariables.HostUserProfile);
                XAssert.IsTrue(!string.IsNullOrEmpty(hostUserProfilePath));
                XAssert.IsTrue(Path.IsPathRooted(hostUserProfilePath));

                string appDataPath = Path.Combine(hostUserProfilePath, "AppData");

                bool userProfileExists = Directory.Exists(hostUserProfilePath);
                bool appDataExists = Directory.Exists(appDataPath);

                // When existence check does not follow the reparse point, then the reparse point is considered
                // as a file. Thus, if the host user profile is a redirected one, then the host user profile does not exist as a directory,
                // but exists as a file.
                bool userProfileNoFollowExists = FileUtilities.DirectoryExistsNoFollow(hostUserProfilePath);

                // The app data is not a reparse point, so existence check that does not follow the reparse point
                // still considers it as a directory.
                bool appDataNoFollowExists = FileUtilities.DirectoryExistsNoFollow(appDataPath);

                // Do batch assert so that we get all the values at once.
                XAssert.IsTrue(
                    userProfileExists 
                    && appDataExists 
                    && (userProfileNoFollowExists != VmSpecialEnvironmentVariables.IsHostUserProfileRedirected)
                    && appDataNoFollowExists,
                    $"{getResult(hostUserProfilePath, userProfileExists)}"
                    + $", {getResult(appDataPath, appDataExists)}"
                    + $", {getResult(hostUserProfilePath, userProfileNoFollowExists, follow: false)}"
                    + $", {getResult(appDataPath, appDataNoFollowExists, follow: false)}");

                if (VmSpecialEnvironmentVariables.IsHostUserProfileRedirected)
                {
                    Possible<ReparsePointType> mayBeReparsePointType = FileUtilities.TryGetReparsePointType(hostUserProfilePath);
                    XAssert.PossiblySucceeded(mayBeReparsePointType);

                    // Ensure that the reparse point of the redirected user profile is a junction, and not a directory symlink.
                    // The reparse point needs to be a junction because junction is evaluted on the machine where the junction is created.
                    // Let's say that we have a junction from 'D:\dbs\BuildXLUserProfile' to the real user profile 'C:\Users\CBA-123'.
                    // When evaluated, 'C:\Users\CBA-123' will be a path in the host, and not in the VM. If it is a directory symlink,
                    // then 'C:\Users\CBA-123' will be the one in the VM, which obviously won't exist.
                    XAssert.AreEqual(ReparsePointType.MountPoint, mayBeReparsePointType.Result);
                }
            });

            static string getResult(string path, bool value, bool follow = true)
            {
                string method = follow ? "exists" : "exists_no_follow";
                return $"{method}('{path}') = {value}";
            }
        }

        [Fact]
        public void VmTempFolderIsLocalInVmIfTempVarIsSet()
        {
            EnsureRunInVm(() => 
            {
                // For our unit test, we ensure that we have redirected temp folder.
                string vmTemp = Environment.GetEnvironmentVariable(VmSpecialEnvironmentVariables.VmTemp);
                XAssert.IsNotNull(vmTemp);
                XAssert.IsTrue(vmTemp.StartsWith(VmConstants.Temp.Root), $"%{VmSpecialEnvironmentVariables.VmTemp}%: '{vmTemp}'");

                string vmOriginalTemp = Environment.GetEnvironmentVariable(VmSpecialEnvironmentVariables.VmOriginalTemp);
                XAssert.IsNotNull(vmOriginalTemp);

                string testEnvironment = Environment.GetEnvironmentVariable("TESTENVIRONMENT");
                bool inQTest = !string.IsNullOrEmpty(testEnvironment) && string.Equals(testEnvironment, "QTEST", StringComparison.OrdinalIgnoreCase);
                
                string tempVarValue = Environment.GetEnvironmentVariable("Temp") ?? Environment.GetEnvironmentVariable("Tmp");
                string tempPath = Path.GetTempPath();

                // For our unit test, we ensure that temp var is set.
                XAssert.IsNotNull(tempVarValue);

                if (inQTest)
                {
                    // QTest changes %TEMP% to its sandbox path. When we relocate the TEMP in sandboxed process pip executor, we don't know about
                    // the existence of QTest sandbox path. The sandbox path may have been created using the original %TEMP%.
                    XAssert.AreEqual(
                        Path.GetPathRoot(vmOriginalTemp).ToUpperInvariant(),
                        Path.GetPathRoot(tempVarValue).ToUpperInvariant(),
                        $"%TEMP% (or %TMP%): '{tempVarValue}'");
                    XAssert.AreEqual(
                        Path.GetPathRoot(vmOriginalTemp).ToUpperInvariant(),
                        Path.GetPathRoot(tempPath.ToUpperInvariant()),
                        $"Path.GetTempPath(): '{tempPath}'");
                }
                else
                {
                    XAssert.IsTrue(tempVarValue.StartsWith(VmConstants.Temp.Root, StringComparison.OrdinalIgnoreCase), $"%TEMP% (or %TMP%): '{tempVarValue}'");
                    XAssert.IsTrue(tempPath.StartsWith(VmConstants.Temp.Root, StringComparison.OrdinalIgnoreCase), $"Path.GetTempPath(): '{tempPath}'");
                }
            });
        }

        private void EnsureRunInVm(Action verify)
        {
            if (VmSpecialEnvironmentVariables.IsRunningInVm)
            {
                verify();
            }
        }
    }
}
