// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using BuildXL.Utilities.Authentication;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Newtonsoft.Json;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests the CredentialProviderHelper. Credential helpers are only used on Windows by BuildXL.
    /// </summary>
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public class CredentialProviderHelperTests : XunitBuildXLTest
    {
        public CredentialProviderHelperTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestSuccessfulPatAcquisition()
        {
            var outStream = new StringBuilder();
            var testAuthOutput = new AuthOutput()
            {
                Username="",
                Password="testPassword",
                Message=""
            };
            var serialized = JsonConvert.SerializeObject(testAuthOutput);
            var credHelper = CredentialProviderHelper.CreateInstanceForTesting(m => outStream.AppendLine(m), CmdHelper.OsShellExe, $"/d /c echo {serialized}");

            var result = credHelper.AcquirePatAsync(new Uri("https://foo"), PatType.CacheReadWrite).Result;

            XAssert.IsTrue(result.Result == CredentialHelperResultType.Success);
            XAssert.IsTrue(outStream.ToString().Contains("Credentials were successfully retrieved from provider"));
            XAssert.IsTrue(outStream.ToString().Contains("testPassword"));
            XAssert.IsNotNull(result.Pat);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestBadCredentialHelperPath(bool emptyPath)
        {
            var outStream = new StringBuilder();
            var credHelper = CredentialProviderHelper.CreateInstanceForTesting(m => outStream.AppendLine(m), emptyPath ? "" : "badPath", "");

            var result = credHelper.AcquirePatAsync(new Uri("https://foo"), PatType.CacheReadWrite).Result;

            XAssert.IsTrue(result.Result == CredentialHelperResultType.NoCredentialProviderSpecified);
            XAssert.IsNull(result.Pat);
        }

        [Fact]
        public void TestBadExitCode()
        {
            var outStream = new StringBuilder();
            var credHelper = CredentialProviderHelper.CreateInstanceForTesting(m => outStream.AppendLine(m), CmdHelper.OsShellExe, "/d /c exit 1");

            var result = credHelper.AcquirePatAsync(new Uri("https://foo"), PatType.CacheReadWrite).Result;

            XAssert.IsTrue(result.Result == CredentialHelperResultType.BadStatusCodeReturned);
            XAssert.IsTrue(outStream.ToString().Contains("Credential provider execution failed with exit code 1"));
            XAssert.IsNull(result.Pat);
        }
    }
}