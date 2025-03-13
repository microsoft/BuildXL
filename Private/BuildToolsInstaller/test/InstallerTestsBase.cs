// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Runtime.CompilerServices;
using BuildToolsInstaller.Utilities;

namespace BuildToolsInstaller.Tests
{
    public abstract class InstallerTestsBase : TestBase
    {

        // Used in the config below and in tests that use that as the verion descriptor
        protected const string GeneralPublicVersion = "0.1.0-20250124.2";
        protected const string GeneralPublicRing = "GeneralPublic";

        /// <summary>
        /// Writes a mock version of the configuration to a temporary path
        /// </summary>
        protected string WriteMockedConfiguration([CallerMemberName] string caller = "")
        {
            var directory = GetTempPathForTest(caller);
            var configForTool = Path.Combine(ConfigurationUtilities.GetConfigurationPathForTool(directory, BuildTool.BuildXL));
            Directory.CreateDirectory(configForTool);
            File.WriteAllText(Path.Combine(configForTool, "deployment-config.json"),
 @$"{{
  ""Rings"": {{
    ""Dogfood"": {{
      ""Version"": ""0.1.0-20250124.3""
    }},
    ""GeneralPublic"": {{
      ""Version"": ""{GeneralPublicVersion}""
    }},
    ""Golden"": {{
      ""Version"": ""0.1.0-20250124.1""
    }}
  }},
  ""Packages"": {{
     ""Linux"": ""BuildXL.linux-x64"",
     ""Windows"": ""BuildXL.win-x64""
    }},
   ""Default"": ""GeneralPublic""
}}");
            File.WriteAllText(Path.Combine(configForTool, "overrides.json"), "{ \"Overrides\": [] }");
            return directory;
        }
    }
}
