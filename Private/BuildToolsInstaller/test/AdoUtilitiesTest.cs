// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildToolsInstaller.Utilities;
using Xunit;

namespace BuildToolsInstaller.Tests
{
    public class AdoUtilitiesTest : TestBase
    {
        [Fact]
        public void AdoEnvironmentTest()
        {
            // We may modify the environment during the test, this will restore it on dispose
            using var modifyEnvironment = new TemporaryTestEnvironment();

            // If we're not in ADO, this will simulate that we are
            var toolsDirectory = GetTempPathForTest();
            modifyEnvironment.Set("AGENT_TOOLSDIRECTORY", toolsDirectory);
            modifyEnvironment.Set("SYSTEM_COLLECTIONURI", "https://mseng.visualstudio.com/");
            modifyEnvironment.Set("TF_BUILD", "True");

            var adoService = AdoService.Instance;
            Assert.True(adoService.IsEnabled);
            Assert.Equal(toolsDirectory, adoService.ToolsDirectory);
            Assert.True(adoService.TryGetOrganizationName(out var orgName));
            Assert.Equal("mseng", orgName);
        }
    }
}
