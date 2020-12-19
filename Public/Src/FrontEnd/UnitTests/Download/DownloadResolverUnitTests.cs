// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Download;
using BuildXL.FrontEnd.Download.Tracing;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(MaxParallelThreads = 1)]

namespace Test.BuildXL.FrontEnd.Download
{
    public class DownloadResolverUnitTests : DsTest
    {
        private const string TestServer = "http://localhost:9753/";

        private readonly RequestCount m_webRequestCount = new RequestCount();
        private readonly AlternativeDataIndicator m_alternativeDataIndicator = new AlternativeDataIndicator();

        private string m_uniqueTestFolder = Guid.NewGuid().ToString();

        public DownloadResolverUnitTests(ITestOutputHelper output)
            : base(output, usePassThroughFileSystem: true)
        {
            RegisterEventSource(global::BuildXL.FrontEnd.Download.ETWLogger.Log);
        }

        [Fact]
        public Task TestNoServer()
        {
            var data = GetSampleData(TestServer + "file.zip", DownloadArchiveType.File);
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    var result = await resolver.DownloadFile(data);

                    // Expect a failure since the server is not launched yet.
                    Assert.True(result.IsErrorValue);
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(1, resolver.Statistics.Downloads.Failures.Count);
                    Assert.Equal(0, m_webRequestCount.Count);

                    SetExpectedFailures(1, 0, "Failed to download id 'TestDownload'");
                },
                useHttpServer: false
            );
        }

        [Fact]
        public Task TestFileNotFound()
        {
            var data = GetSampleData(TestServer + "file.404", DownloadArchiveType.File);
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    var result = await resolver.DownloadFile(data);

                    // Expect a failure since the server is not launched yet.
                    Assert.True(result.IsErrorValue);
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(1, resolver.Statistics.Downloads.Failures.Count);
                    Assert.Equal(1, m_webRequestCount.Count);

                    SetExpectedFailures(1, 0, "404 (Not Found)");
                }
            );
        }

        [Fact]
        public Task TestOnlyFile()
        {
            var data = GetSampleData(TestServer + "file.txt", DownloadArchiveType.File);
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    var result = await resolver.DownloadFile(data);

                    var filePath = ((FileArtifact)result.Value).Path.ToString(FrontEndContext.PathTable);
                    // Expect a failure since the server is not launched yet.
                    Assert.True(File.Exists(filePath));
                    Assert.Equal("Hello World", File.ReadAllText(filePath));
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.Failures.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.Total.Count);
                    Assert.Equal(1, m_webRequestCount.Count);
                }
            );
        }

        [Fact]
        public Task TestMultipleAccessOnlyOnceExecuted()
        {
            var data = GetSampleData(TestServer + "file.zip", DownloadArchiveType.Zip);
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    await resolver.DownloadFile(data);
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.Total.Count);

                    await resolver.DownloadFile(data);
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.Total.Count);

                    await resolver.ExtractFile(data);
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(1, resolver.Statistics.Extractions.Total.Count);

                    await resolver.ExtractFile(data);
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(1, resolver.Statistics.Extractions.Total.Count);

                    await resolver.DownloadFile(data);
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(1, resolver.Statistics.Extractions.Total.Count);
                }
            );
        }

        [Fact]
        public Task FileExtractionShouldNotExtract()
        {
            var data = GetSampleData(TestServer + "file.txt", DownloadArchiveType.File);
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    await resolver.ExtractFile(data);
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.Total.Count);
                }
            );
        }

        [Fact]
        public Task TestZipExtraction()
        {
            var data = GetSampleData(TestServer + "file.zip", DownloadArchiveType.Zip);
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    var extract = await resolver.ExtractFile(data);
                    var extractStaticDir = (StaticDirectory)extract.Value;
                    var extractFolder = extractStaticDir.Path.ToString(FrontEndContext.PathTable);

                    Assert.Equal(3, extractStaticDir.Contents.Length);

                    var worldFile = Path.Combine(extractFolder, "world");
                    Assert.True(File.Exists(worldFile));
                    Assert.Equal("Hello World", File.ReadAllText(worldFile));

                    var galaxyFile = Path.Combine(extractFolder, "galaxy");
                    Assert.True(File.Exists(galaxyFile));
                    Assert.Equal("Hello Galaxy", File.ReadAllText(galaxyFile));

                    var universeFile = Path.Combine(extractFolder, "multi", "universe");
                    Assert.True(File.Exists(universeFile));
                    Assert.Equal("Hello Universe", File.ReadAllText(universeFile));

                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.Failures.Count);
                    Assert.Equal(1, resolver.Statistics.Extractions.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.Failures.Count);
                    Assert.Equal(1, m_webRequestCount.Count);
                }
            );
        }

        [Fact]
        public Task FileExtractionWithWrongHashShouldFail()
        {
            var data = GetSampleData(TestServer + "file.txt", DownloadArchiveType.File, ContentHash.Random(HashType.Vso0));
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    await resolver.DownloadFile(data);
                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(1, resolver.Statistics.Downloads.Failures.Count);

                    SetExpectedFailures(1, 0, "data on the server has been altered");
                }
            );
        }

        [Fact]
        public Task SameHashWillBeIncrementalAndDifferentHashWillReDownload()
        {
            ContentHash.TryParse("VSO0:E87891E21CD24671B953BAB6A2D6F9C91049C67F9EA0E5015620C3DBC766EDC500", out var helloWorldHash);
            ContentHash.TryParse("VSO0:1D6240B6C13AC7B412F81EF6BF26A529C8D9B6BF3EC6D3F9E5305EB922F050F700", out var helloGalaxyHash);

            var data = GetSampleData(TestServer + "file.txt", DownloadArchiveType.File, helloWorldHash);
            var downloadedFile = data.DownloadedFilePath.ToString(FrontEndContext.PathTable);
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    await resolver.PerformDownloadOrIncrementalCheckAsync(data);

                    Assert.Equal(1, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.SkippedDueToManifest.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.Failures.Count);
                    Assert.Equal(1, m_webRequestCount.Count);

                    Assert.True(File.Exists(downloadedFile));
                    Assert.Equal("Hello World", File.ReadAllText(downloadedFile));
                    Assert.False(File.Exists(data.DownloadManifestFile.ToString(FrontEndContext.PathTable)));

                    await resolver.PerformDownloadOrIncrementalCheckAsync(data);

                    Assert.Equal(2, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(1, resolver.Statistics.Downloads.SkippedDueToManifest.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.Failures.Count);
                    Assert.Equal(1, m_webRequestCount.Count);

                    // Force update to server
                    m_alternativeDataIndicator.UseAlternativeData = true;
                    var newData = GetSampleData(TestServer + "file.txt", DownloadArchiveType.File, helloGalaxyHash);

                    await resolver.PerformDownloadOrIncrementalCheckAsync(newData);

                    Assert.True(File.Exists(downloadedFile));
                    Assert.Equal("Hello Galaxy", File.ReadAllText(downloadedFile));
                    Assert.Equal(3, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(1, resolver.Statistics.Downloads.SkippedDueToManifest.Count);
                    Assert.Equal(2, m_webRequestCount.Count);

                    await resolver.PerformDownloadOrIncrementalCheckAsync(newData);

                    Assert.Equal(4, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(2, resolver.Statistics.Downloads.SkippedDueToManifest.Count);
                    Assert.Equal(2, m_webRequestCount.Count);
                }
            );
        }

        [Fact]
        public Task DownloadIncrementalChecks()
        {
            var data = GetSampleData(TestServer + "file.txt", DownloadArchiveType.File);
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    int expectedCount = 0;

                    var downloadedFile = data.DownloadedFilePath.ToString(FrontEndContext.PathTable);
                    var manifestFile = data.DownloadManifestFile.ToString(FrontEndContext.PathTable);

                    // Initial download should download
                    await resolver.PerformDownloadOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, m_webRequestCount.Count);
                    Assert.Equal(expectedCount, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.SkippedDueToManifest.Count);

                    // Should re-download if we modify the download manifest
                    File.WriteAllText(manifestFile, "Botched");
                    await resolver.PerformDownloadOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, m_webRequestCount.Count);
                    Assert.Equal(expectedCount, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.SkippedDueToManifest.Count);

                    // Should re-download if we remove the download manifest
                    File.Delete(manifestFile);
                    await resolver.PerformDownloadOrIncrementalCheckAsync(data);
                    expectedCount++;

                    // Should re-download if we modify the downloaded file.
                    File.WriteAllText(downloadedFile, "Modified");
                    await resolver.PerformDownloadOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, m_webRequestCount.Count);
                    Assert.Equal(expectedCount, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.SkippedDueToManifest.Count);

                    // Should re-download if we remove the downloaded
                    File.Delete(downloadedFile);
                    await resolver.PerformDownloadOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, m_webRequestCount.Count);
                    Assert.Equal(expectedCount, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.SkippedDueToManifest.Count);

                    Assert.Equal(expectedCount, m_webRequestCount.Count);
                    Assert.Equal(expectedCount, resolver.Statistics.Downloads.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Downloads.SkippedDueToManifest.Count);

                    // nothing should have failed
                    Assert.Equal(0, resolver.Statistics.Extractions.Failures.Count);
                }
            );
        }

        [Fact]
        public Task ExtractIncrementalChecks()
        {
            var data = GetSampleData(TestServer + "file.zip", DownloadArchiveType.Zip);
            return TestDownloadResolver(
                data,
                async resolver =>
                {
                    var extractedFolder = data.ContentsFolder.Path.ToString(FrontEndContext.PathTable);
                    var manifestFile = data.ExtractManifestFile.ToString(FrontEndContext.PathTable);

                    int expectedCount = 0;

                    // Initial case should extract
                    await resolver.PerformExtractOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, resolver.Statistics.Extractions.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.SkippedDueToManifest.Count);

                    // Should re-extract when modifying manifest
                    File.WriteAllText(manifestFile, "Botched");
                    await resolver.PerformExtractOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, resolver.Statistics.Extractions.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.SkippedDueToManifest.Count);

                    // Should re-extract when deleting manifest
                    File.Delete(manifestFile);
                    await resolver.PerformExtractOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, resolver.Statistics.Extractions.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.SkippedDueToManifest.Count);

                    // Deleting one of the extracted files should re-extract
                    File.Delete(Path.Combine(extractedFolder, "world"));
                    await resolver.PerformExtractOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, resolver.Statistics.Extractions.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.SkippedDueToManifest.Count);

                    // Modifying one of the extracted files should re-extract
                    File.WriteAllText(Path.Combine(extractedFolder, "world"), "moon");
                    await resolver.PerformExtractOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, resolver.Statistics.Extractions.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.SkippedDueToManifest.Count);

                    // Deleting one of the extracted files in a sub-folder should re-extract
                    File.WriteAllText(Path.Combine(extractedFolder, "multi", "universe"), "level-4");
                    await resolver.PerformExtractOrIncrementalCheckAsync(data);
                    expectedCount++;

                    Assert.Equal(expectedCount, resolver.Statistics.Extractions.Total.Count);
                    Assert.Equal(0, resolver.Statistics.Extractions.SkippedDueToManifest.Count);
                }
            );
        }

        private async Task TestDownloadResolver(DownloadData data, Func<DownloadResolver, Task> performTest, bool useHttpServer = true)
        {
            var dummyConfigFile = Path.Combine(TemporaryDirectory, m_uniqueTestFolder, "config.dsc");

            var statistics = new Statistics();
            var moduleRegistry = new ModuleRegistry(FrontEndContext.SymbolTable);
            var configuration = ConfigurationHelpers.GetDefaultForTesting(FrontEndContext.PathTable, AbsolutePath.Create(FrontEndContext.PathTable, dummyConfigFile));
            var resolverSettings = new ResolverSettings();

            var frontEndFactory = new FrontEndFactory();
            frontEndFactory.AddFrontEnd(new DownloadFrontEnd());
            frontEndFactory.TrySeal(new LoggingContext("UnitTest"));

            using (var host = new FrontEndHostController(
                frontEndFactory,
                new EvaluationScheduler(degreeOfParallelism: 1),
                moduleRegistry,
                new FrontEndStatistics(),
                global::BuildXL.FrontEnd.Core.Tracing.Logger.CreateLogger(),
                collector: null,
                collectMemoryAsSoonAsPossible: false))
            {

                var frontEndEngineAbstraction = new BasicFrontEndEngineAbstraction(
                    FrontEndContext.PathTable,
                    FrontEndContext.FileSystem,
                    configuration);

                ((IFrontEndController)host).InitializeHost(FrontEndContext, configuration);
                host.SetState(frontEndEngineAbstraction, new TestEnv.TestPipGraph(), configuration);

                var resolver = new DownloadResolver(
                    statistics,
                    host,
                    FrontEndContext,
                    Logger.Log,
                    "TestFrontEnd"
                );

                var workspaceResolver = new DownloadWorkspaceResolver();
                workspaceResolver.UpdateDataForDownloadData(data, FrontEndContext);
                await resolver.InitResolverAsync(resolverSettings, workspaceResolver);

                if (useHttpServer)
                {
                    using (var listener = new HttpListener())
                    {
                        // This test relies on the mutex in the build engine to only run one unittest at a time and this assembly to be single thread
                        // if any of those assumptions will be broken we will have to either dynamically (remind you globally) get unique ports.
                        // HttpListner doesn't have this built-in so there will always be a race. Just spam the ports utnill one doesn't fail
                        // use a global mutex (This is not honored by qtest since it can run in a different session on cloudbuild).
                        listener.Prefixes.Add(TestServer);
                        listener.Start();

                        TestRequestHandler.StartRequestHandler(listener, m_alternativeDataIndicator, m_webRequestCount);

                        await performTest(resolver);

                        listener.Stop();
                        listener.Close();
                    }
                }
                else
                {
                    await performTest(resolver);
                }
            }
        }

        private DownloadData GetSampleData(string url, DownloadArchiveType archiveType, ContentHash? contentHash = null)
        {
            var uri = new Uri(url);
            return new DownloadData(
                FrontEndContext,
                new DownloadFileSettings
                {
                    ModuleName = "TestDownload",
                    ArchiveType = archiveType,
                    Url = url,
                },
                uri,
                AbsolutePath.Create(FrontEndContext.PathTable, Path.Combine(TemporaryDirectory, m_uniqueTestFolder, "DownloadCache")),
                contentHash);
        }
    }
}