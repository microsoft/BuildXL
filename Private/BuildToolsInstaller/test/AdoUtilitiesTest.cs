// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildToolsInstaller.Utiltiies;
using Xunit;

namespace BuildToolsInstaller.Tests
{
    public class AdoUtilitiesTest
    {
        [Fact]
        public void AdoEnvironmentTest()
        {
            // We may modify the environment during the test, this will restore it on dispose
            using var modifyEnvironment = new TemporaryTestEnvironment();

            // If we're not in ADO, this will simulate that we are
            var toolsDirectory = Path.Combine(Path.GetTempPath(), "Test");
            modifyEnvironment.Set("AGENT_TOOLSDIRECTORY", toolsDirectory);
            modifyEnvironment.Set("SYSTEM_COLLECTIONURI", "https://mseng.visualstudio.com/");
            modifyEnvironment.Set("TF_BUILD", "True");

            Assert.True(AdoUtilities.IsAdoBuild);
            Assert.Equal(toolsDirectory, AdoUtilities.ToolsDirectory);
            Assert.True(AdoUtilities.TryGetOrganizationName(out var orgName));
            Assert.Equal("mseng", orgName);
        }
    }
}
