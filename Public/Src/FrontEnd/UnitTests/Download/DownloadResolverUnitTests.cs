// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

[assembly: CollectionBehavior(MaxParallelThreads = 1)]

namespace Test.BuildXL.FrontEnd.Download
{
    public class DownloadResolverCredentialProviderTests : DownloadResolverIntegrationTestBase
    {
        private const string CredentialProviderEnv = "NUGET_CREDENTIALPROVIDERS_PATH";

        protected override EnginePhases Phase => EnginePhases.Schedule;

        public DownloadResolverCredentialProviderTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OnlyCredentialProviderExecutableIsUntracked(bool executablePath)
        {
            string providerDirectory = Path.Combine(TestRoot, "Provider With Spaces");
            Directory.CreateDirectory(providerDirectory);
            string providerExecutable = Path.Combine(providerDirectory, "CredentialProvider.Microsoft.exe");
            File.WriteAllText(providerExecutable, "credential provider");
            string otherDirectory = Path.Combine(TestRoot, "Another Provider");
            Directory.CreateDirectory(otherDirectory);
            string providerPaths = string.Join(Path.PathSeparator.ToString(),
                otherDirectory, executablePath ? providerExecutable : providerDirectory);
            var config = Build(new[] { GetSampleData("http://localhost:9753/file.zip", DownloadArchiveType.Zip) }).PersistSpecsAndGetConfiguration();
            ((CommandLineConfiguration)config).Startup.Properties[CredentialProviderEnv] = providerPaths;

            var result = RunEngine(config);
            Assert.True(result.IsSuccess);
            var processes = result.EngineState.RetrieveProcesses().ToArray();
            var download = processes.Single(process => process.Tags.Contains(StringId.Create(FrontEndContext.StringTable, "download")));
            var extract = processes.Single(process => process.Tags.Contains(StringId.Create(FrontEndContext.StringTable, "extract")));
            foreach (string directory in new[] { providerDirectory, otherDirectory })
            {
                var path = AbsolutePath.Create(FrontEndContext.PathTable, directory);
                Assert.DoesNotContain(path, download.UntrackedScopes);
                Assert.DoesNotContain(path, extract.UntrackedScopes);
            }
            Assert.DoesNotContain(download.EnvironmentVariables, variable =>
                !variable.IsPassThrough && variable.Name.ToString(FrontEndContext.StringTable) == CredentialProviderEnv);
            var executable = AbsolutePath.Create(FrontEndContext.PathTable, providerExecutable);
            Assert.Contains(executable, download.UntrackedPaths);
            Assert.DoesNotContain(executable, extract.UntrackedPaths);
            Assert.True(download.HasUntrackedChildProcesses);
            Assert.Empty(download.ChildProcessesToBreakawayFromSandbox);
            Assert.Empty(extract.ChildProcessesToBreakawayFromSandbox);
            Assert.False(extract.HasUntrackedChildProcesses);
            Assert.False(download.AllowUndeclaredSourceReads);
            Assert.True(config.Sandbox.FailUnexpectedFileAccesses);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnresolvableCredentialProviderDoesNotBlockAnonymousDownloads(bool invalidPath)
        {
            string providerPath = invalidPath ? "relative-provider" : Path.Combine(TestRoot, "Empty Provider");
            if (!invalidPath)
            {
                Directory.CreateDirectory(providerPath);
            }

            var config = Build(new[] { GetSampleData("http://localhost:9753/file.txt", DownloadArchiveType.File) }).PersistSpecsAndGetConfiguration();
            ((CommandLineConfiguration)config).Startup.Properties[CredentialProviderEnv] = providerPath;
            var result = RunEngine(config);

            Assert.True(result.IsSuccess);
            AssertWarningEventLogged(global::BuildXL.FrontEnd.Download.Tracing.LogEventId.CredentialProviderDiscoveryFailed);
        }
    }

    public class DownloadResolverUnitTests : DownloadResolverIntegrationTestBase
    {
        private const string TestServer = "http://localhost:9753/";

        public DownloadResolverUnitTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestNoServer()
        {
            var data = GetSampleData(TestServer + "file.zip", DownloadArchiveType.File);

            var configuration = Build(new[] { data }).PersistSpecsAndGetConfiguration();
            var result = RunEngine(configuration);

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessError);

            string error = EventListener.GetLogMessagesForEventId((int)global::BuildXL.Processes.Tracing.LogEventId.PipProcessError).Single();
            Assert.Contains("refused", error);
        }

        [Fact]
        public void TestFileNotFound()
        {
            var data = GetSampleData(TestServer + "file.404", DownloadArchiveType.File);

            var configuration = Build(new[] { data }).PersistSpecsAndGetConfiguration();
            var result = RunEngineWithServer(configuration);

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessError);

            string error = EventListener.GetLogMessagesForEventId((int)global::BuildXL.Processes.Tracing.LogEventId.PipProcessError).Single();
            Assert.Contains("404 (Not Found)", error);
        }

        [Fact]
        public void TestOnlyFile()
        {
            var data = GetSampleData(TestServer + "file.txt", DownloadArchiveType.File);

            var configuration = Build(new[] { data }).PersistSpecsAndGetConfiguration();
            var result = RunEngineWithServer(configuration);

            Assert.True(result.IsSuccess);

            var filePath = result.EngineState.RetrieveProcesses().Single().FileOutputs.Single().Path.ToString(FrontEndContext.PathTable);
            Assert.True(File.Exists(filePath));
            Assert.Equal("Hello World", File.ReadAllText(filePath));
        }

        [Fact]
        public void FileExtractionShouldNotExtract()
        {
            var data = GetSampleData(TestServer + "file.txt", DownloadArchiveType.File);

            var configuration = Build(new[] { data }).PersistSpecsAndGetConfiguration();
            var result = RunEngineWithServer(configuration);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.EngineState.RetrieveProcesses().Count());
        }

        [Fact]
        public void TestZipExtraction()
        {
            var data = GetSampleData(TestServer + "file.zip", DownloadArchiveType.Zip);

            var configuration = Build(new[] { data }).PersistSpecsAndGetConfiguration();
            var result = RunEngineWithServer(configuration);

            // retrieve the single extract pip based on tags
            var extractPip = result.EngineState.RetrieveProcesses()
                .Where(process => process.Tags.Contains(global::BuildXL.Utilities.Core.StringId.Create(FrontEndContext.StringTable, "extract"))).Single();
            var extractFolder = extractPip.DirectoryOutputs.Single().Path.ToString(FrontEndContext.PathTable);
            
            var worldFile = Path.Combine(extractFolder, "world");
            Assert.True(File.Exists(worldFile));
            Assert.Equal("Hello World", File.ReadAllText(worldFile));

            var galaxyFile = Path.Combine(extractFolder, "galaxy");
            Assert.True(File.Exists(galaxyFile));
            Assert.Equal("Hello Galaxy", File.ReadAllText(galaxyFile));

            var universeFile = Path.Combine(extractFolder, "multi", "universe");
            Assert.True(File.Exists(universeFile));
            Assert.Equal("Hello Universe", File.ReadAllText(universeFile));

            Assert.Equal(3, Directory.EnumerateFileSystemEntries(extractFolder).Count());
        }

        [Fact]
        public void FileExtractionWithWrongHashShouldFail()
        {
            var data = GetSampleData(TestServer + "file.txt", DownloadArchiveType.File, ContentHash.Random(HashType.Vso0));

            var configuration = Build(new[] { data }).PersistSpecsAndGetConfiguration();
            var result = RunEngineWithServer(configuration);

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessError);

            string error = EventListener.GetLogMessagesForEventId((int)global::BuildXL.Processes.Tracing.LogEventId.PipProcessError).Single();
            Assert.Contains("data on the server has been altered", error);
        }

        [Fact]
        public void DeclaredRequiredFilesAreRequiredOutputsAndCanBeConsumed()
        {
            var data = new DownloadFileSettings(GetSampleData(TestServer + "file.zip", DownloadArchiveType.Zip))
            {
                ExtractedValueName = "payload",
                RequiredFiles = new[]
                {
                    RelativePath.Create(FrontEndContext.StringTable, "world"),
                    RelativePath.Create(FrontEndContext.StringTable, "multi/universe"),
                },
            };

            string spec = @"
import {Transformer} from 'Sdk.Transformers';
const files = importFrom('TestDownload').payloadFiles;
const objectRoot = Context.getMount('ObjectRoot').path;
const first = Transformer.copyFile(files[0], p`${objectRoot}/world-copy`);
const second = Transformer.copyFile(files[1], p`${objectRoot}/universe-copy`);
";
            var configuration = Build(new[] { data })
                .AddSpec("test.dsc", spec)
                .PersistSpecsAndGetConfiguration();

            var result = RunEngineWithServer(configuration);
            Assert.True(result.IsSuccess);

            var extractPip = result.EngineState.RetrieveProcesses()
                .Single(process => process.Tags.Contains(StringId.Create(FrontEndContext.StringTable, "extract")));
            Assert.Equal(2, extractPip.FileOutputs.Length);
            Assert.All(extractPip.FileOutputs, file => Assert.True(file.IsRequiredOutputFile));
            Assert.Empty(result.EngineState.PipGraph.GetExistenceAssertionsUnderOpaqueDirectory(extractPip.DirectoryOutputs.Single()));
            string extractRoot = extractPip.DirectoryOutputs.Single().Path.ToString(FrontEndContext.PathTable);
            Assert.True(File.Exists(Path.Combine(extractRoot, "galaxy")));

            string objectRoot = configuration.Layout.ObjectDirectory.ToString(FrontEndContext.PathTable);
            Assert.Equal("Hello World", File.ReadAllText(Path.Combine(objectRoot, "world-copy")));
            Assert.Equal("Hello Universe", File.ReadAllText(Path.Combine(objectRoot, "universe-copy")));
        }

        [Theory]
        [InlineData(DownloadArchiveType.File, false, false)]
        [InlineData(DownloadArchiveType.Zip, true, false)]
        [InlineData(DownloadArchiveType.Zip, false, true)]
        public void InvalidExtractedFileSettingsAreRejected(DownloadArchiveType archiveType, bool nameCollision, bool duplicatePath)
        {
            var world = RelativePath.Create(FrontEndContext.StringTable, "world");
            var data = new DownloadFileSettings(GetSampleData(TestServer + "file.zip", archiveType))
            {
                DownloadedValueName = nameCollision ? "payloadFiles" : null,
                ExtractedValueName = "payload",
                RequiredFiles = duplicatePath ? new[] { world, world } : new[] { world },
            };

            var configuration = Build(new[] { data }).PersistSpecsAndGetConfiguration();
            Assert.False(RunEngine(configuration).IsSuccess);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Download.Tracing.LogEventId.InvalidRequiredFiles);
        }

        [Fact]
        public void MissingDeclaredExtractedFileFailsExecution()
        {
            var data = new DownloadFileSettings(GetSampleData(TestServer + "file.zip", DownloadArchiveType.Zip))
            {
                RequiredFiles = new[] { RelativePath.Create(FrontEndContext.StringTable, "missing") },
            };

            var configuration = Build(new[] { data }).PersistSpecsAndGetConfiguration();

            Assert.False(RunEngineWithServer(configuration).IsSuccess);
            AssertVerboseEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessMissingExpectedOutputOnCleanExit);
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessExpectedMissingOutputs);
            AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessError);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void ExtractionGetsProperlyExposed()
        {
            var data = GetSampleData(TestServer + "file.zip", DownloadArchiveType.Zip);

            // Consume the extracted files with a copy directory pip
            string spec = @"
import {Transformer} from 'Sdk.Transformers';
const extracted = importFrom('TestDownload').extracted;
const result = Transformer.copyDirectory({sourceDir: extracted.root, targetDir: Context.getNewOutputDirectory('target'), dependencies: [extracted]});
";

            var configuration = Build(new[] { data })
                .AddSpec("test.dsc", spec)
                .PersistSpecsAndGetConfiguration();
            var result = RunEngineWithServer(configuration);

            Assert.True(result.IsSuccess);

            // retrieve the copy pip 
            var copyPip = result.EngineState.RetrieveProcesses()
                .Where(process => process.ToolDescription.IsValid && process.ToolDescription.ToString(FrontEndContext.StringTable).Contains("Copy Directory")).Single();
            var targetDir = copyPip.DirectoryOutputs.Single().Path.ToString(FrontEndContext.PathTable);

            // Make sure the three files were copied to the target directory
            Assert.Equal(3, Directory.EnumerateFileSystemEntries(targetDir).Count());
        }
    }
}