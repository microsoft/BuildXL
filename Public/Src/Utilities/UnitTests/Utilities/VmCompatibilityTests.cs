// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

        private void EnsureRunInVm(Action verify)
        {
            if (VmSpecialEnvironmentVariables.IsRunningInVm)
            {
                verify();
            }
        }
    }
}
