// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using BuildToolsInstaller.Config;
using BuildToolsInstaller.Utiltiies;
using Xunit;

namespace BuildToolsInstaller.Tests
{
    public class DeploymentConfigurationTests
    {
        private const string TestConfiguration = @"
{
    ""rings"": [
        {
            ""name"": ""Dogfood"",
            ""description"": ""Dogfood ring"",
            ""tools"": {
                ""BuildXL"": { ""version"": ""0.1.0-20250101.1"" }
            }
        },
        {
            ""name"": ""GeneralPublic"",
            ""tools"": {
                ""BuildXL"": { ""version"": ""0.1.0-20241025.4"" }
            }
        }
    ],

    ""overrides"":
    [ 
        {
            ""comment"": ""description of the pin"",
            ""repository"": ""1JS"",
            ""tools"": {
                ""BuildXL"": { ""version"": ""0.1.0-20240801.1"" }
            }
        },
        {
            ""repository"": ""BuildXL.Internal"",
            ""pipelineIds"": [10101, 1010],
            ""tools"": {
                ""BuildXL"": { ""version"": ""0.1.0-20240801.1"" }
            }
        }
    ]
}
";

        [Fact]
        public void DeserializationTest()
        {
            var deserialized = JsonSerializer.Deserialize<DeploymentConfiguration>(TestConfiguration, JsonUtilities.DefaultSerializerOptions);
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.Overrides);
            Assert.NotNull(deserialized.Rings);
            Assert.Equal(2, deserialized.Overrides.Count);
            Assert.Equal(2, deserialized.Rings.Count);
            Assert.Equal("Dogfood", deserialized.Rings[0].Name);
            Assert.Equal("Dogfood ring", deserialized.Rings[0].Description);
            Assert.Contains(BuildTool.BuildXL, deserialized.Rings[0].Tools.Keys);
            Assert.Equal("0.1.0-20250101.1", deserialized.Rings[0].Tools[BuildTool.BuildXL].Version);
            Assert.Equal("description of the pin", deserialized.Overrides[0].Comment);
            Assert.Equal("1JS", deserialized.Overrides[0].Repository);
            Assert.Contains(BuildTool.BuildXL, deserialized.Overrides[0].Tools.Keys);
            Assert.Equal("0.1.0-20240801.1", deserialized.Overrides[0].Tools[BuildTool.BuildXL].Version);
            Assert.NotNull(deserialized.Overrides[1].PipelineIds);
            Assert.Equal(2, deserialized.Overrides[1].PipelineIds!.Count);
        }

        [Fact]
        public void ResolutionByRingTest()
        {
            var config = new DeploymentConfiguration()
            {
                Rings = [
                    new RingDefinition() {
                        Name = "A",
                        Tools = new ReadOnlyDictionary<BuildTool, ToolDeployment>(new Dictionary<BuildTool, ToolDeployment> () {
                            { BuildTool.BuildXL, new ToolDeployment() { Version = "VersionA" }  }
                        })
                    },
                    new RingDefinition() {
                        Name = "B",
                        Tools = new ReadOnlyDictionary<BuildTool, ToolDeployment>(new Dictionary<BuildTool, ToolDeployment> () {
                            { BuildTool.BuildXL, new ToolDeployment() { Version = "VersionB" }  }
                        })
                    }
                ]
            };

            var mockAdoService = new MockAdoService() { ToolsDirectory = Path.GetTempPath() };
            Assert.Equal("VersionA", ConfigurationUtilities.ResolveVersion(config, "A", BuildTool.BuildXL, mockAdoService, new TestLogger()));
            Assert.Equal("VersionB", ConfigurationUtilities.ResolveVersion(config, "B", BuildTool.BuildXL, mockAdoService, new TestLogger()));
            Assert.Null(ConfigurationUtilities.ResolveVersion(config, "C", BuildTool.BuildXL, mockAdoService, new TestLogger()));
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

            var config = new DeploymentConfiguration()
            {
                Rings = [
                    new RingDefinition() {
                        Name = "A",
                        Tools = new ReadOnlyDictionary<BuildTool, ToolDeployment>(new Dictionary<BuildTool, ToolDeployment> () {
                            { BuildTool.BuildXL, new ToolDeployment() { Version = "VersionA" }  }
                        })
                    }
                ],
                Overrides = [
                    new DeploymentOverride() {
                        Repository = pinByRepo ? pinnedRepo : "not_ " + pinnedRepo,
                        PipelineIds = pinByPipeline ? [ pinnedPipeline ] : null,
                        Tools = new ReadOnlyDictionary<BuildTool, ToolDeployment>(new Dictionary<BuildTool, ToolDeployment> () {
                            { BuildTool.BuildXL, new ToolDeployment() { Version = "PinnedVersion" }  }
                        })
                    }
                ]
            };

            var mockAdoService = new MockAdoService()
            {
                ToolsDirectory = Path.GetTempPath(),
                RepositoryName = pinnedRepo,
                PipelineId = pinnedPipeline
            };

            var expected = pinByRepo ? "PinnedVersion" : "VersionA";
            Assert.Equal(expected, ConfigurationUtilities.ResolveVersion(config, "A", BuildTool.BuildXL, mockAdoService, new TestLogger()));
        }
    }
}
