// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Runtime.CompilerServices;

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
            Directory.CreateDirectory(Path.Combine(directory, "buildxl"));
            File.WriteAllText(Path.Combine(directory, "buildxl", "rings.json"),
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
   ""Default"": ""GeneralPublic""
}}");
            File.WriteAllText(Path.Combine(directory, "buildxl", "overrides.json"), "{ \"Overrides\": [] }");
            return directory;
        }
    }
}
