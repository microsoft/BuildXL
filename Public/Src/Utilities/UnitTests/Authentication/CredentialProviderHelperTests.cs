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
using System.Collections.Generic;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests the CredentialProviderHelper. Credential helpers are only used on Windows by BuildXL.
    /// </summary>
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public class CredentialProviderHelperTests : XunitBuildXLTest
    {
        private static readonly Dictionary<CredentialHelperType, object> s_outputMap = new Dictionary<CredentialHelperType, object>()
        {
            // [SuppressMessage("Microsoft.Security", "CS002:SecretInNextLine", Justification="Not a real password, testing the json output format")]
            { CredentialHelperType.Cloudbuild, new GenericAuthOutput() { Username = "", Password = "testValue", Message = "" } },
            { CredentialHelperType.MicrosoftAuthenticationCLI, new AzureAuthOutput() { User = "", DisplayName = "", Token = "testValue", ExpirationDate = "" } }
        };

        public CredentialProviderHelperTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(CredentialHelperType.Cloudbuild)]
        [InlineData(CredentialHelperType.MicrosoftAuthenticationCLI)]
        public void TestSuccessfulTokenAcquisition(CredentialHelperType outputType)
        {
            var outStream = new StringBuilder();
            var serialized = JsonConvert.SerializeObject(s_outputMap[outputType]);
            var credHelper = CredentialProviderHelper.CreateInstanceForTesting(m => outStream.AppendLine(m), CmdHelper.OsShellExe, $"/d /c echo {serialized}", outputType);

            var result = credHelper.AcquireTokenAsync(new Uri("https://foo"), PatType.CacheReadWrite).Result;

            XAssert.IsTrue(result.Result == CredentialHelperResultType.Success);
            XAssert.IsTrue(outStream.ToString().Contains("Credentials were successfully retrieved from provider"));
            XAssert.IsFalse(string.IsNullOrEmpty(result.Token));
            XAssert.AreEqual(result.Token, "testValue");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestBadCredentialHelperPath(bool emptyPath)
        {
            var outStream = new StringBuilder();
            var credHelper = CredentialProviderHelper.CreateInstanceForTesting(m => outStream.AppendLine(m), emptyPath ? "" : "badPath", "", CredentialHelperType.Cloudbuild);

            var result = credHelper.AcquireTokenAsync(new Uri("https://foo"), PatType.CacheReadWrite).Result;

            XAssert.IsTrue(result.Result == CredentialHelperResultType.NoCredentialProviderSpecified);
            XAssert.IsTrue(string.IsNullOrEmpty(result.Token));
        }

        [Fact]
        public void TestBadExitCode()
        {
            var outStream = new StringBuilder();
            var credHelper = CredentialProviderHelper.CreateInstanceForTesting(m => outStream.AppendLine(m), CmdHelper.OsShellExe, "/d /c exit 1", CredentialHelperType.Cloudbuild);

            var result = credHelper.AcquireTokenAsync(new Uri("https://foo"), PatType.CacheReadWrite).Result;

            XAssert.IsTrue(result.Result == CredentialHelperResultType.BadStatusCodeReturned);
            XAssert.IsTrue(outStream.ToString().Contains("Credential provider execution failed with exit code 1"));
            XAssert.IsTrue(string.IsNullOrEmpty(result.Token));
        }
    }
}