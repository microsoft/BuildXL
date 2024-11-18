// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Test parsing of linux distro info
    /// </summary>
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public sealed class ParseLinuxDistroInfoTests
    {
        /// <summary>
        /// Checks if valid linux distro information, including versionId and distroName, is correctly parsed and matches the expected values.
        /// </summary>
        [Theory]
        [MemberData(nameof(ParsingTestData.SuccessfulDistoInfoData), MemberType = typeof(ParsingTestData))]
        public void TestParseLinuxDistroInfo(List<string> parsingContent, Version expectedVersionId, string expectedDistroName)
        {
            var distroInfo = LinuxSystemInfo.ParseLinuxDistroInfo(parsingContent);
            XAssert.AreEqual(expectedVersionId, distroInfo.distroVersionId);
            XAssert.AreEqual(expectedDistroName, distroInfo.distroName);
        }

        /// <summary>
        /// Ensures that invalid or incomplete linux distro info is handled accordingly.
        /// </summary>
        [Theory]
        [MemberData(nameof(ParsingTestData.UnSuccessfulDistoInfoData), MemberType = typeof(ParsingTestData))]
        public void TestIncorrectLinuxDistroInfoParsing(List<string> parsingContent, string expectedExceptionMessage)
        {
            var exceptionThrown = false;

            try
            {
                var distroInfo = LinuxSystemInfo.ParseLinuxDistroInfo(parsingContent);
            }
            catch (Exception ex)
            {
                exceptionThrown = true;
                XAssert.Contains(ex.ToString(), expectedExceptionMessage);
            }

            XAssert.IsTrue(exceptionThrown);
        }

        /// <summary>
        /// Test data for validating various scenarios related to the parsing of linux distro info.
        /// </summary>
        internal static class ParsingTestData
        {
            public static readonly List<string> ParseValidDistroNameAndVersionId = new()
            {
                "VERSION=\"22.04.3 LTS (Jammy Jellyfish)\"",
                "ID=ubuntu",
                "ID_LIKE=debian",
                "PRETTY_NAME=\"Ubuntu 22.04.3 LTS\"",
                "VERSION_ID=\"22.04\"",
                "VERSION_CODENAME=jammy",
                "UBUNTU_CODENAME=jammy"
            };

            public static readonly List<string> ParseSingleVersionId = new()
            {
                "VERSION=\"22 LTS (Jammy Jellyfish)\"",
                "ID=ubuntu",
                "ID_LIKE=debian",
                "PRETTY_NAME=\"Ubuntu 22 LTS\"",
                "VERSION_ID=\"22\"",
                "VERSION_CODENAME=jammy",
                "UBUNTU_CODENAME=jammy"
            };

            public static readonly List<string> ParseInvalidVersionId = new()
            {
                "VERSION=\"Invalid Version\"",
                "ID=ubuntu",
                "ID_LIKE=debian",
                "PRETTY_NAME=\"Ubuntu 22.04.3 LTS\"",
                "VERSION_ID=\"Invalid\"",
                "VERSION_CODENAME=jammy",
                "UBUNTU_CODENAME=jammy"
            };

            public static readonly List<string> ParseInvalidDistroName = new()
            {
                "VERSION=\"Invalid Version\"",
                "ID=\"\"",
                "ID_LIKE=debian",
                "PRETTY_NAME=\"Ubuntu 22.04.3 LTS\"",
                "VERSION_ID=\"22.04\"",
                "VERSION_CODENAME=jammy",
                "UBUNTU_CODENAME=jammy"
            };

            public static readonly List<string> ParseMissingVersionId = new()
            {
                "VERSION=\"Invalid Version\"",
                "ID=ubuntu",
                "ID_LIKE=debian",
                "PRETTY_NAME=\"Ubuntu Invalid Version\"",
                "VERSION_CODENAME=jammy",
                "UBUNTU_CODENAME=jammy"
            };

            public static readonly List<string> ParseMissingDistroName = new()
            {
                "VERSION=\"Invalid Version\"",
                "ID_LIKE=debian",
                "PRETTY_NAME=\"Ubuntu 22.04.3 LTS\"",
                "VERSION_ID=\"22.04\"",
                "VERSION_CODENAME=jammy",
                "UBUNTU_CODENAME=jammy"
            };

            public static readonly List<string> ParseMissingVersionIdAndDistroName = new()
            {
                "VERSION=\"Invalid Version\"",
                "ID_LIKE=debian",
                "PRETTY_NAME=\"Ubuntu Invalid Version\"",
                "VERSION_CODENAME=jammy",
                "UBUNTU_CODENAME=jammy"
            };

            public static readonly List<string> ParseInvalidVersionIdAndMissingDistroName = new()
            {
                "VERSION=\"Invalid Version\"",
                "ID_LIKE=debian",
                "PRETTY_NAME=\"Ubuntu 22.04.3 LTS\"",
                "VERSION_ID=\"Invalid\"",
                "VERSION_CODENAME=jammy",
                "UBUNTU_CODENAME=jammy"
            };

            public static IEnumerable<object[]> SuccessfulDistoInfoData =>
                new List<object[]>
                {
                    new object[] { ParseValidDistroNameAndVersionId, new Version("22.04"), "ubuntu" },
                    new object[] { ParseSingleVersionId, new Version("22.0"), "ubuntu" },
                };

            public static IEnumerable<object[]> UnSuccessfulDistoInfoData =>
                new List<object[]>
                {
                    new object[] { ParseInvalidVersionId, LinuxSystemInfo.InvalidVersionIdExceptionMessage },
                    new object[] { ParseInvalidDistroName, LinuxSystemInfo.InvalidDistroInfoExceptionMessage },
                    new object[] { ParseMissingVersionId, LinuxSystemInfo.InvalidDistroInfoExceptionMessage },
                    new object[] { ParseMissingDistroName, LinuxSystemInfo.InvalidDistroInfoExceptionMessage },
                    new object[] { ParseMissingVersionIdAndDistroName, LinuxSystemInfo.InvalidDistroInfoExceptionMessage },
                    new object[] { ParseInvalidVersionIdAndMissingDistroName, LinuxSystemInfo.InvalidVersionIdExceptionMessage },
                };
        }
    }
}
