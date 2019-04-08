// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class OperatingSystemHelperTests
    {
        [Fact]
        public void FrameworkVersionShouldBeAvailableOnWindowsBox()
        {
            string frameworkAsText = OperatingSystemHelper.GetInstalledDotNetFrameworkVersion();

            var noNetFrameworkIsDetected = "No .NET Framework is detected";
            if (OperatingSystemHelper.IsUnixOS)
            {
                Assert.Equal(noNetFrameworkIsDetected, frameworkAsText);
            }
            else
            {
                Assert.NotEqual(noNetFrameworkIsDetected, frameworkAsText);
            }

        }
    }
}