// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using ContentStoreTest.Test;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Azure.Core;

namespace BuildXL.Cache.ContentStore.Test.Auth
{
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public class CodespacesCredentialsTests : TestBase
    {
        // CODESYNC: Public/Src/Cache/ContentStore/Test/BuildXL.Cache.ContentStore.Test.dsc
        private string AuthHelperPath => Path.Combine(TestDeploymentDir, AzureAuthTokenCredential.AuthHelperToolName);

        public CodespacesCredentialsTests() : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void ToolLookupReturnsNullWhenAbsent()
        {
            string envVarName = "_ToolLookupReturnsNullWhenAbsent";
            Environment.SetEnvironmentVariable(envVarName, "/path/to/nothing");

            var authHelperPath = AzureAuthTokenCredential.FindAuthHelperToolForTesting(envVarName, out _);
            Assert.Null(authHelperPath);
        }

        [Fact]
        public void ToolLookupReturnsLocationWhenPresent()
        {
            // Set the env var that is going to be used as PATH to contain the directory
            // where we deployed the mock azure auth tool
            string envVarName = "_ToolLookupReturnsLocationWhenPresent";
            Environment.SetEnvironmentVariable(envVarName, TestDeploymentDir);

            var authHelperPath = AzureAuthTokenCredential.FindAuthHelperToolForTesting(envVarName, out _);
            Assert.Equal(AuthHelperPath, authHelperPath);
        }

        [Fact]
        public void RequestTokenWithoutScopes()
        {
            var azureCredential = new AzureAuthTokenCredential(AuthHelperPath);
            var token = azureCredential.GetToken(new TokenRequestContext(), CancellationToken.None);
            Assert.Equal("This is a mock bearer token for scopes ''", token.Token);
        }

        [Fact]
        public void RequestTokenWithScopes()
        {
            var azureCredential = new AzureAuthTokenCredential(AuthHelperPath);
            var token = azureCredential.GetToken(new TokenRequestContext(scopes: ["MyScope", "MyOtherScope"]), CancellationToken.None);
            Assert.Equal("This is a mock bearer token for scopes 'MyScope MyOtherScope'", token.Token);
        }
    }
}
