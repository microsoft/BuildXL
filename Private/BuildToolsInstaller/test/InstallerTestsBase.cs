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
        protected const string DefaultVersion = "0.1.0-20250124.2";
        protected const string DefaultRing = "Ring1";
        protected const string ToolName = "MyTool";

        /// <summary>
        /// Writes a mock version of the configuration to a temporary path
        /// </summary>
        protected string WriteMockedConfiguration([CallerMemberName] string caller = "")
        {
            var directory = GetTempPathForTest(caller);
            var configForTool = Path.Combine(ConfigurationUtilities.GetConfigurationPathForTool(directory, ToolName));
            Directory.CreateDirectory(configForTool);
            File.WriteAllText(Path.Combine(configForTool, "deployment-config.json"),
 @$"{{
  ""Rings"": {{
    ""Ring0"": {{
      ""Version"": ""0.1.0-20250124.3""
    }},
    ""{DefaultRing}"": {{
      ""Version"": ""{DefaultVersion}""
    }},
    ""Ring2"": {{
      ""Version"": ""0.1.0-20250124.1""
    }}
  }},
  ""Packages"": {{
     ""Linux"": ""{ToolName}.linux-x64"",
     ""Windows"": ""{ToolName}.win-x64""
    }},
   ""Default"": ""Ring1""
}}");
            File.WriteAllText(Path.Combine(configForTool, "overrides.json"), "{ \"Overrides\": [] }");
            return directory;
        }
    }
}
