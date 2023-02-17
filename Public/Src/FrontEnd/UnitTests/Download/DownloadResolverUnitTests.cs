// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(MaxParallelThreads = 1)]

namespace Test.BuildXL.FrontEnd.Download
{
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