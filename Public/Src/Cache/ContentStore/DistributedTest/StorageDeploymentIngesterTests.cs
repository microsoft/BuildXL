// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NETCOREAPP

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.Host.Service.Deployment;
using BuildXL.Launcher.Server;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Microsoft.Azure.Amqp.Framing;
using Xunit;
using Xunit.Abstractions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using RelativePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.RelativePath;

namespace BuildXL.Cache.ContentStore.Distributed.Test
{
    [Collection("Redis-based tests")]
    public class StorageDeploymentIngesterTests : TestBase
    {
        public OperationContext Context;

        private readonly LocalRedisFixture _fixture;
        public const string DropToken = "FAKE_DROP_TOKEN";
        public StorageDeploymentIngesterTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(TestGlobal.Logger, output)
        {
            _fixture = fixture;
            Context = new OperationContext(new Interfaces.Tracing.Context(Logger));
        }

        private class TestDeploymentConfig
        {
            public List<DropDeploymentConfiguration> Drops { get; } = new List<DropDeploymentConfiguration>();

            public class DropDeploymentConfiguration : Dictionary<string, string>
            {
            }
        }

        private static readonly string ConfigString = @"
{
    'Drops': [
        {
            'BaseUrl[Stage:1]': 'https://dev.azure.com/buildxlcachetest/drop/drops/deployment/stage1',
            'BaseUrl[Stage:2]': 'https://dev.azure.com/buildxlcachetest/drop/drops/deployment/stage2',

            'RelativeRoot[Tool:A]' : 'tools/toola',
            'RelativeRoot[Tool:B]' : 'app/appb',
            'RelativeRoot[Tool:C]' : 'c',


            'Url [Ring:Ring_0]': 'https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop1?root=release/win-x64',
            'Url [Ring:Ring_1]': 'https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop2?root=debug',
            'Url [Ring:Ring_2]': 'https://dev.azure.com/buildxlcachetest/drop/drops/dev/testdrop1?root=release/win-x64',
            'TargetRelativePath': 'bin'
        },
        {
            'Url': 'file://Env',
        },
        {
            'TargetRelativePath': 'info',
            'Url [Stamp:ST_S1]': 'file://Files/Foo.txt',
            'Url [Stamp:ST_S2]': 'file://Env/Foo.txt',
            'Url [Stamp:ST_S3]': 'file://Stamp3',
        }
    ],
    'AzureStorageSecretInfo': { 'Name': 'myregionalStorage{Region:LA}', 'TimeToLive':'60m' },
    'SasUrlTimeToLive': '3m',
    'Tool [Environment:MyEnvRunningOnWindows]': {
        'Executable': 'bin/service.exe',
        'Arguments': [ 'myargs' ],
        'EnvironmentVariables': {
            'ConfigPath': '../Foo.txt'
        }
    }
}
".Replace("'", "\"");

        private record StorageAccountInfo(string ConnectionString, string ContainerName)
        {
            public string Region { get; set; }
            public string VirtualAccountName { get; set; }

            // Set this to try against real storage.
            public static string OverrideConnectionString { get; } = null;

            public string ConnectionString { get; } = OverrideConnectionString ?? ConnectionString;

            // The current Azurite version we use supports up to this version
            public BlobContainerClient Container { get; } = new BlobContainerClient(ConnectionString, ContainerName, new BlobClientOptions(BlobClientOptions.ServiceVersion.V2020_02_10));

            public async Task<BlobContainerClient> GetContainerAsync()
            {
                await Container.CreateIfNotExistsAsync();

                if (OverrideConnectionString == null)
                {
                    return Container;
                }

                var uri = Container.GenerateSasUri(Azure.Storage.Sas.BlobContainerSasPermissions.All, DateTimeOffset.Now.AddDays(1));
                return new BlobContainerClient(uri, null);
            }
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // TODO: investigate why
        public async Task TestFullDeployment()
        {
            using var s1 = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, TestGlobal.Logger);
            using var s2 = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, TestGlobal.Logger);
            using var s3 = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, TestGlobal.Logger);

            var storageMap = new Dictionary<string, StorageAccountInfo[]>()
            {
                {
                    "westus2",
                    new StorageAccountInfo[]
                    {
                        new(s1.ConnectionString, "container1"),
                        new(s1.ConnectionString, "container2"),
                        new(s1.ConnectionString, "container3"),
                    }
                },
                {
                    "centralus",
                    new StorageAccountInfo[]
                    {
                        new(s2.ConnectionString, "container4"),
                        new(s2.ConnectionString, "container5"),
                    }
                },
                {
                    "eastus2",
                    new StorageAccountInfo[]
                    {
                        new(s3.ConnectionString, "container6"),
                        new(s3.ConnectionString, "container7"),
                    }
                }
            };

            var storageByAccountName = storageMap
                .SelectMany(kvp => kvp.Value.Select((account, index) => account with { VirtualAccountName = $"{kvp.Key}_{account.ContainerName}", Region = kvp.Key }))
                .ToDictionary(a => a.VirtualAccountName);

            var ingesterConfiguration = new IngesterConfiguration()
            {
                StorageAccountsByRegion = storageByAccountName.Values.GroupBy(a => a.Region).ToDictionary(e => e.Key, e => e.Select(a => a.VirtualAccountName).ToArray()),
                ContentContainerName = "testcontainer"
            };

            var sources = new Dictionary<string, string>()
            {
                { @"Stamp3\info.txt", "" },

                { @"Env\RootFile.json", "{ 'key1': 1, 'key2': 2 }" },
                { @"Env\Subfolder\Hello.txt", "Hello world" },
                { @"Env\Foo.txt", "Baz" },

                { @"Files\Foo.txt", "Bar" },
            };

            Dictionary<string, string> getSubDrop(Dictionary<string, string> dropContents, string root, string prefix)
            {
                return dropContents.Where(e => e.Key.StartsWith(root.Replace("/", "\\")))
                    .ToDictionary(e => e.Key.Substring((prefix ?? root).Length), e => e.Value);
            }

            Dictionary<string, string> getSourceDrop(string root, string prefix)
            {
                return getSubDrop(sources, root, prefix);
            }

            var baseUrlDrops = new Dictionary<string, Dictionary<string, string>>()
            {
                {
                    "https://dev.azure.com/buildxlcachetest/drop/drops/deployment/stage1",
                    new Dictionary<string, string>
                    {
                        { @"tools\toola\info.txt", "" },
                        { @"app\appb\subfolder\file.json", "{ 'key1': 1, 'key2': 2 }" },
                        { @"app\appb\Hello.txt", "Hello world" },
                        { @"c\Foo.txt", "Baz" },
                        { @"c\Bar.txt", "Bar" },
                    }
                },
                {
                    "https://dev.azure.com/buildxlcachetest/drop/drops/deployment/stage2",
                    new Dictionary<string, string>
                    {
                        { @"tools\toola\info.txt", "Information appears hear now" },
                        { @"app\appb\subfolder\file.json", "{ 'key1': 3, 'key2': 4 }" },
                        { @"app\appb\subfolder\newfile.json", "{ 'prop': 'this is a new file', 'key2': 4 }" },
                        { @"app\appb\Hello.txt", "Hello world" },
                        { @"c\Foo.txt", "Baz" },
                        { @"c\Bar.txt", "Bar" },
                    }
                },
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
                },
                {
                    DeploymentUtilities.ConfigDropUri.OriginalString,
                    new Dictionary<string, string>()
                    {
                        { DeploymentUtilities.DeploymentConfigurationFileName, ConfigString }
                    }
                },
                {
                    "file://Env", getSourceDrop(@"Env\", @"Env\")
                },
                {
                    "file://Files/Foo.txt", getSourceDrop(@"Files\Foo.txt", @"Files\")
                },
                {
                    "file://Env/Foo.txt", getSourceDrop(@"Env\Foo.txt", @"Env\")
                },
                {
                    "file://Stamp3", getSourceDrop(@"Stamp3\", @"Stamp3\")
                }
            };

            var deploymentRoot = TestRootDirectoryPath / "deploy";
            var ingester = new StorageDeploymentIngester(
                Context,
                configuration: ingesterConfiguration,
                sourceRoot: base.TestRootDirectoryPath / "src",
                deploymentRoot: deploymentRoot,
                deploymentConfigurationPath: base.TestRootDirectoryPath / "DeploymentConfiguration.json",
                FileSystem,
                dropExeFilePath: base.TestRootDirectoryPath / @"dropbin\drop.exe",
                dropToken: DropToken);

            // Write source files
            WriteFiles(ingester.SourceRoot, sources);

            FileSystem.WriteAllText(ingester.DeploymentConfigurationPath, ConfigString);

            Dictionary<string, string> getDropContents(string dropUrl, string relativeRoot = null)
            {
                var uri = new UriBuilder(dropUrl);
                var query = uri.Query;
                uri.Query = null;

                if (relativeRoot == null && query != null)
                {
                    relativeRoot = HttpUtility.ParseQueryString(query)["root"];
                }

                return baseUrlDrops.TryGetValue(uri.Uri.ToString(), out var contents)
                    ? getSubDrop(contents, relativeRoot, prefix: "")
                    : drops[dropUrl];
            }

            ingester.OverrideLaunchDropProcess = t =>
            {
                var dropContents = getDropContents(t.dropUrl, t.relativeRoot);
                WriteFiles(new AbsolutePath(t.targetDirectory) / (t.relativeRoot ?? ""), dropContents);
                return BoolResult.Success;
            };

            ingester.OverrideGetContainer = t =>
            {
                return storageByAccountName[t.accountName].GetContainerAsync();
            };

            await ingester.RunAsync().ShouldBeSuccess();

            var manifestText = FileSystem.ReadAllText(ingester.DeploymentManifestPath);
            var deploymentManifest = JsonSerializer.Deserialize<DeploymentManifest>(manifestText);

            foreach (var drop in drops)
            {
                var uri = new Uri(drop.Key);
                var expectedDropContents = drops[drop.Key];
                var layoutSpec = deploymentManifest.Drops[drop.Key];
                layoutSpec.Count.Should().Be(expectedDropContents.Count);
                foreach (var fileAndContent in expectedDropContents)
                {
                    var hash = new ContentHash(layoutSpec[fileAndContent.Key].Hash);
                    var expectedPath = ingester.DeploymentRoot / DeploymentUtilities.GetContentRelativePath(hash);

                    foreach (var account in storageByAccountName.Values)
                    {
                        var blob = account.Container.GetBlobClient(DeploymentUtilities.GetContentRelativePath(hash).ToString());
                        var content = await blob.DownloadContentAsync();
                        content.Value.Details.ContentLength.Should().Be(Encoding.UTF8.GetByteCount(fileAndContent.Value));
                        if (fileAndContent.Value.Length > 0)
                        {
                            var text = content.Value.Content.ToString();
                            text.Should().Be(fileAndContent.Value);
                        }
                    }
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

        private class TestSecretsProvider : ISecretsProvider
        {
            public Task<RetrievedSecrets> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token)
            {
                var secrets = new Dictionary<string, Secret>();

                foreach (var request in requests)
                {
                    if (request.Kind == SecretKind.PlainText)
                    {
                        secrets.Add(request.Name, new PlainTextSecret($"https://{request.Name}.azure.blob.com/{Guid.NewGuid()}"));
                    }
                    else
                    {
                        request.Kind.Should().Be(SecretKind.SasToken);
                        secrets.Add(
                            request.Name,
                            new UpdatingSasToken(
                                new SasToken(
                                    storageAccount: $"https://{request.Name}.azure.blob.com/",
                                    resourcePath: "ResourcePath",
                                    token: Guid.NewGuid().ToString())));
                    }
                }

                return Task.FromResult(new RetrievedSecrets(secrets));
            }
        }
    }
}

#endif