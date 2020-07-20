// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                
                string tempVarValue = Environment.GetEnvironmentVariable("Temp") ?? Environment.GetEnvironmentVariable("Tmp");
                string tempPath = Path.GetTempPath();

                // For our unit test, we ensure that temp var is set.
                XAssert.IsNotNull(tempVarValue);
                
                XAssert.IsTrue(tempVarValue.StartsWith(VmConstants.Temp.Root, StringComparison.OrdinalIgnoreCase), $"%TEMP% (or %TMP%): '{tempVarValue}'");
                XAssert.IsTrue(tempPath.StartsWith(VmConstants.Temp.Root, StringComparison.OrdinalIgnoreCase), $"Path.GetTempPath(): '{tempPath}'");                
            });
        }

        [Fact]
        public void VmPipCurrentlyDoesNotHaveUserProfileEnvironmentVariablesSpecified()
        {
            EnsureRunInVm(() =>
            {
                var userProfileEnvs = new[]
                {
                    getEnvironment("UserName"),
                    getEnvironment("UserProfile"),
                    getEnvironment("AppData"),
                    getEnvironment("LocalAppData"),
                    getEnvironment("HomeDrive"),
                    getEnvironment("HomePath")
                };

                var existingEnvs = userProfileEnvs.Where(e => !string.IsNullOrEmpty(e.value));
                XAssert.AreEqual(
                    0,
                    existingEnvs.Count(),
                    string.Join("; ", existingEnvs.Select(e => $"%{e.name}%: \"{e.value}\"")));
            });

            static (string name, string value) getEnvironment(string name) => (name, Environment.GetEnvironmentVariable(name));
        }

        [Fact]
        public void VmPipHasSpecialEnvironmentVariables()
        {
            EnsureRunInVm(() =>
            {
                var envs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in Environment.GetEnvironmentVariables().Keys)
                {
                    envs[key.ToString()] = Environment.GetEnvironmentVariable(key.ToString());
                }

                var verifyEqual = new (string name, string value)[]
                {
                    (VmSpecialEnvironmentVariables.IsInVm, "1"),
                    (VmSpecialEnvironmentVariables.HostHasRedirectedUserProfile, "1"),
                };

                foreach (var (name, value) in verifyEqual)
                {
                    assertExists(envs, name);
                    assertEqual(name, value, envs[name]);
                }

                var verifyPrefix = new[]
                {
                    (VmSpecialEnvironmentVariables.VmTemp, VmConstants.Temp.Root)
                };

                foreach (var (name, value) in verifyPrefix)
                {
                    assertExists(envs, name);
                    assertPrefix(name, envs[name], value);
                }

                var verifyRoot = new[]
                {
                    (VmSpecialEnvironmentVariables.VmOriginalTemp, VmConstants.Host.NetUseDrive + @":\")
                };

                foreach (var (name, value) in verifyRoot)
                {
                    assertExists(envs, name);
                    assertRoot(name, envs[name], value);
                }
            });

            static void assertExists(IReadOnlyDictionary<string, string> envs, string name)
            {
                XAssert.IsTrue(envs.ContainsKey(name), $"%{name}% does not exist in VM");
            }

            static void assertEqual(string name, string expected, string actual)
            {
                XAssert.AreEqual(expected, actual, $"%{name}%: (expected: '{expected}', actual: '{actual}')");
            }

            static void assertPrefix(string name, string value, string prefix)
            {
                XAssert.IsTrue(value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), $"%{name}%: '{value}' does not start with '{prefix}'");
            }

            static void assertRoot(string name, string path, string expectedRoot)
            {
                var root = Path.GetPathRoot(path); // From B:\..., this returns B:\. We cannot omit this step because the temp B:\...\temp is redirected to T:\...
                XAssert.IsTrue(FileUtilities.TryGetFinalPathNameByPath(root, out var finalPath, out var error)); // Final path should be UNC\<ip-address>\D\...
                var finalRoot = finalPath.Split(Path.DirectorySeparatorChar)[2] + @":\";
                XAssert.AreEqual(expectedRoot.ToUpperInvariant(), finalRoot.ToUpperInvariant(), $"%{name}%: (expected root: '{expectedRoot.ToUpperInvariant()}', actual root: '{finalRoot.ToUpperInvariant()}')");
            }
        }

        [Fact]
        public void HardCodedUserProfileEnvironmentVariablesHaveExpectedValues()
        {
            EnsureRunInVm(() =>
            {
                foreach (var env in VmConstants.UserProfile.Environments)
                {
                    if (env.Value.toVerify != null)
                    {
                        XAssert.AreEqual(
                            env.Value.value.ToUpperInvariant(),
                            env.Value.toVerify().ToUpperInvariant(),
                            $"Mismatched values for %{env.Key}%. Expected: '{env.Value.value}', Actual: '{env.Value.toVerify()}'");
                    }
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
