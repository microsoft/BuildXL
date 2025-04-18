﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using BuildToolsInstaller.Config;
using BuildToolsInstaller.Utilities;
using Xunit;

namespace BuildToolsInstaller.Tests
{
    public class DeploymentConfigurationTests : TestBase
    {
        private const string ToolName = "MyTool";
        private const string TestOverrideConfig = @$"
{{
""Overrides"":
    [ 
        {{
            ""Comment"": ""description of the pin"",
            ""Repository"": ""SpecialRepo"",
            ""Tools"": {{
                ""{ToolName}"": {{ ""Version"": ""0.1.0-20240801.1"" }}
            }}
        }},
        {{
            ""Repository"": ""AnotherRepository"",
            ""PipelineIds"": [10101, 1010],
            ""Tools"": {{
                ""{ToolName}"": {{ ""Version"": ""0.1.0-20240801.1"" }}
            }}
        }}
    ]
}}
";

private const string TestConfiguration = @$"
{{
    ""Rings"": {{
        ""Ring0"": 
        {{
             ""Version"": ""0.1.0-20250101.1"",
             ""Description"": ""ring zero""
        }},
        ""Ring1"": 
        {{
            ""Version"": ""0.1.0-20250101.2""
        }}
    }},
    ""Packages"": {{
        ""Linux"": ""{ToolName}.linux-x64"",
        ""Windows"": ""{ToolName}.win-x64""
    }},
    ""Default"": ""Ring1""
}}
";

        [Fact]
        public void DeserializationTestOverrides()
        {
            var deserializedOverrides = JsonSerializer.Deserialize<OverrideConfiguration>(TestOverrideConfig, JsonUtilities.DefaultSerializerOptions);
            Assert.NotNull(deserializedOverrides);
            Assert.Equal(2, deserializedOverrides.Overrides.Count);
            Assert.Equal("description of the pin", deserializedOverrides.Overrides[0].Comment);
            Assert.Equal("SpecialRepo", deserializedOverrides.Overrides[0].Repository);
            Assert.Contains(ToolName, deserializedOverrides.Overrides[0].Tools.Keys);
            Assert.Equal("0.1.0-20240801.1", deserializedOverrides.Overrides[0].Tools[ToolName].Version);
            Assert.NotNull(deserializedOverrides.Overrides[1].PipelineIds);
            Assert.Equal(2, deserializedOverrides.Overrides[1].PipelineIds!.Count);
        }

        [Fact]
        public void DeserializationTest()
        {
            var serializerOptions = new JsonSerializerOptions(JsonUtilities.DefaultSerializerOptions)
            {
                Converters = { new CaseInsensitiveDictionaryConverter<string>() }
            };

            var deserialized = JsonSerializer.Deserialize<DeploymentConfiguration>(TestConfiguration, serializerOptions);
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.Rings);
            Assert.Equal(2, deserialized.Rings.Count);
            Assert.True(deserialized.Rings.ContainsKey("Ring0"));
            Assert.Equal("ring zero", deserialized.Rings["Ring0"].Description);
            Assert.Equal("0.1.0-20250101.1", deserialized.Rings["Ring0"].Version);
            Assert.Equal($"{ToolName}.win-x64", deserialized.Packages.GetValueOrDefault("Windows"));
            Assert.Equal($"{ToolName}.win-x64", deserialized.Packages.GetValueOrDefault("WINDOWS"));
        }

        [Fact]
        public void ResolutionByRingTest()
        {
            var config = new DeploymentConfiguration()
            {
                Rings =
                new Dictionary<string, RingDefinition>() {
                    { "A", new RingDefinition() { Version = "VersionA", IgnoreCache = true } },
                    { "B", new RingDefinition() { Version = "VersionB" } },
                },
                Packages = new Dictionary<string, string> {
                    { "Linux", "LinuxPackage" },
                    {  "Windows", "WindowsPackage" }
                },
                Default = "A"
            };

            var mockAdoService = new MockAdoService() { ToolsDirectory = Path.GetTempPath() };
            Assert.Equal("VersionA", ConfigurationUtilities.ResolveVersion(config, "A", out var ignoreA, mockAdoService, new TestLogger()));
            Assert.True(ignoreA);
            Assert.Equal("VersionB", ConfigurationUtilities.ResolveVersion(config, "B", out var ignoreB, mockAdoService, new TestLogger()));
            Assert.False(ignoreB);
            Assert.Null(ConfigurationUtilities.ResolveVersion(config, "C", out _, mockAdoService, new TestLogger()));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void Exceptions(bool pinByRepo, bool pinByPipeline)
        {
            var pinnedRepo = "PinnedRepo";
            var pinnedPipeline = 1001;
            var overrideConfig = new OverrideConfiguration()
            {
                Overrides = [
                    new DeploymentOverride() {
                        Repository = pinByRepo ? pinnedRepo : "not_ " + pinnedRepo,
                        PipelineIds = pinByPipeline ? [ pinnedPipeline ] : null,
                        Tools = new ReadOnlyDictionary<string, ToolDeployment>(new Dictionary<string, ToolDeployment> () {
                            { ToolName, new ToolDeployment() { Version = "PinnedVersion" }  }
                        })
                    }
                ]
            };

            var mockAdoService = new MockAdoService()
            {
                ToolsDirectory = GetTempPathForTest(),
                RepositoryName = pinnedRepo,
                PipelineId = pinnedPipeline
            };

            Assert.Equal(pinByRepo, ConfigurationUtilities.TryGetOverride(overrideConfig, ToolName, mockAdoService, out var overridden, new TestLogger()));
            if (pinByRepo)
            {
                Assert.Equal("PinnedVersion", overridden);
            }
        }
    }
}
