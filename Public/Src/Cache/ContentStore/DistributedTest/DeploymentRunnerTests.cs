// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test
{
    public class DeploymentRunnerTests : TestBase
    {
        public OperationContext Context;

        public const string DropToken = "FAKE_DROP_TOKEN";
        public DeploymentRunnerTests(ITestOutputHelper output)
            : base(TestGlobal.Logger, output)
        {
            Context = new OperationContext(new Interfaces.Tracing.Context(Logger));
        }

        private class TestDeploymentConfig
        {
            public List<DropDeploymentConfiguration> Drops { get; } = new List<DropDeploymentConfiguration>();

            public class DropDeploymentConfiguration : Dictionary<string, string>
            {
            }
        }
        
        [Fact]
        public async Task TestFullDeployment()
        {
            var sources = new Dictionary<string, string>()
            {
                { @"Env\RootFile.json", "{ 'key1': 1, 'key2': 2 }" },
                { @"Env\Subfolder\Hello.txt", "Hello world" },
                { @"Env\Foo.txt", "Baz" },

                { @"Files\Foo.txt", "Bar" },
            };

            Dictionary<string, string> getSourceDrop(string root, string prefix)
            {
                return sources.Where(e => e.Key.StartsWith(root))
                    .ToDictionary(e => e.Key.Substring(prefix.Length), e => e.Value);
            }

            var expectedSourceDrops = new Dictionary<string, Dictionary<string, string>>()
            {
                {
                    "file://Env", getSourceDrop(@"Env\", @"Env\")
                },
                {
                    "file://Files/Foo.txt", getSourceDrop(@"Files\Foo.txt", @"Files\")
                },
                {
                    "file://Env/Foo.txt", getSourceDrop(@"Env\Foo.txt", @"Env\")
                }
            };

            var drops = new Dictionary<string, Dictionary<string, string>>()
            {
                {
                    "https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop1?root=release/win-x64",
                    new Dictionary<string, string>
                    {
                        { @"file1.bin", "File content 1" },
                        { @"file2.txt", "File content 2" },
                        { @"sub\file3.dll", "File content 3" }
                    }
                },
                {
                    "https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop2?root=debug",
                    new Dictionary<string, string>
                    {
                        { @"file1.bin", "File content 1" },
                        { @"file2.txt", "File content 2 changed" },
                        { @"sub\file5.dll", "File content 5" }
                    }
                }
            };

            var config = new TestDeploymentConfig()
            {
                Drops =
                {
                    new TestDeploymentConfig.DropDeploymentConfiguration()
                    {
                        { "Url [Ring:Ring_0]", "https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop1?root=release/win-x64" },
                        { "Url [Ring:Ring_1]", "https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop2?root=debug" },
                    },
                    new TestDeploymentConfig.DropDeploymentConfiguration()
                    {
                        { "Url", "file://Env" },
                    },
                    new TestDeploymentConfig.DropDeploymentConfiguration()
                    {
                        { "Url [Stamp:DefaultStamp]", "file://Files/Foo.txt" },
                    },
                    new TestDeploymentConfig.DropDeploymentConfiguration()
                    {
                        { "Url [Stamp:DefaultStamp]", "file://Env/Foo.txt" },
                    }
                }
            };

            var deploymentRunner = new DeploymentRunner(
                Context,
                sourceRoot: TestRootDirectoryPath / "src",
                deploymentRoot: TestRootDirectoryPath / "deploy",
                deploymentConfigurationPath: TestRootDirectoryPath / "DeploymentConfiguration.json",
                FileSystem,
                dropExeFilePath: TestRootDirectoryPath / @"dropbin\drop.exe",
                retentionSizeGb: 1,
                dropToken: DropToken);

            // Write source files
            WriteFiles(deploymentRunner.SourceRoot, sources);

            var configText = JsonSerializer.Serialize(config, new JsonSerializerOptions()
            {
                WriteIndented = true
            });

            FileSystem.WriteAllText(deploymentRunner.DeploymentConfigurationPath, configText);

            deploymentRunner.OverrideLaunchDropProcess = t =>
            {
                var dropContents = drops[t.dropUrl];
                WriteFiles(new AbsolutePath(t.targetDirectory) / (t.relativeRoot ?? ""), dropContents);
                return BoolResult.Success;
            };

            await deploymentRunner.RunAsync().ShouldBeSuccess();

            var manifestText = FileSystem.ReadAllText(deploymentRunner.DeploymentManifestPath);
            var deploymentManifest = JsonSerializer.Deserialize<DeploymentManifest>(manifestText);

            foreach (var drop in deploymentManifest.Drops)
            {
                var uri = new Uri(drop.Key);

                var expectedDropContents = uri.IsFile ? expectedSourceDrops[drop.Key] : drops[drop.Key];
                var layoutSpec = deploymentManifest.Drops[drop.Key];
                layoutSpec.Count.Should().Be(expectedDropContents.Count);
                foreach (var fileAndContent in expectedDropContents)
                {
                    var hash = new ContentHash(layoutSpec[fileAndContent.Key].Hash);
                    var expectedPath = deploymentRunner.DeploymentRoot / DeploymentUtilities.GetContentRelativePath(hash);

                    var text = FileSystem.ReadAllText(expectedPath);
                    text.Should().Be(fileAndContent.Value);
                }
            }
        }

        private void WriteFiles(AbsolutePath root, Dictionary<string, string> files)
        {
            foreach (var file in files)
            {
                var path = root / file.Key;
                FileSystem.CreateDirectory(path.Parent);
                FileSystem.WriteAllText(path, file.Value);
            }
        }
    }
}
