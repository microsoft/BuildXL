// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine.Tracing;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.Engine;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;
using FrontEndEventId = BuildXL.FrontEnd.Core.Tracing.LogEventId;

namespace Test.BuildXL.EngineTests
{
    // NOTE: don't add more tests here, use SchedulerIntegrationTestBase instead
    [Trait("Category", "WindowsOSOnly")] // relies heavily on csc.exe
    [Trait("Category", "MiniBuildTester")] // relies on csc deployment.
    public sealed class MiniBuildTester : BaseEngineTest
    {
        public MiniBuildTester(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void MiniBuildHelloWorld()
        {
            // Ignore DX222 for csc.exe being outside of src directory
            IgnoreWarnings();

            SetupHelloWorld();
            RunEngine();

            var objectDirectoryPath = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);

            XAssert.IsTrue(File.Exists(Path.Combine(objectDirectoryPath, "HelloWorld.cs")));
            XAssert.IsTrue(File.Exists(Path.Combine(objectDirectoryPath, "src", "HelloWorld.cs")));
            XAssert.IsTrue(File.Exists(Path.Combine(objectDirectoryPath, "bin", "HelloWorld.exe")));

            string outPath = Path.Combine(objectDirectoryPath, "HelloWorld.out");
            XAssert.IsTrue(File.Exists(outPath));
            string s = File.ReadAllText(outPath);
            XAssert.IsTrue(s.Equals("Hello World, BuildXL!", StringComparison.Ordinal));
        }

        /// <summary>
        /// The lifetime of the scheduler is different when no pips match the filter. Historically we've had a bunch of
        /// crash bugs in this case. Make sure BuildXL doesn't crash.
        /// </summary>
        [Fact]
        public void MiniBuildNoPipsMatchFilter()
        {
            IgnoreWarnings();
            SetupHelloWorld();
            Configuration.Filter = "tag='IDontMatchAnything'";

            RunEngine(expectSuccess: false);

            AssertErrorEventLogged(EventId.NoPipsMatchedFilter);
        }

        [Fact]
        public void HelloWorldVsSolutionGeneration()
        {
            // Relatively simple test to ensure the /VS generation feature doesn't crash
            Configuration.Ide.IsEnabled = true;
            Configuration.Ide.IsNewEnabled = true;
            SetupHelloWorld();
            RunEngine();

            var outputDirectoryPath = Configuration.Layout.OutputDirectory.ToString(Context.PathTable);
            XAssert.IsTrue(File.Exists(Path.Combine(
                outputDirectoryPath, 
                "vs",
                Configuration.Ide.IsNewEnabled ? "srcNew" : "src",
                Configuration.Ide.IsNewEnabled ? "srcNew.sln" : "src.sln")));
        }

        [Fact]
        public void MiniBuildCachedGraph()
        {
            SetupMiniBuild();

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;


            RunEngine("First build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            DeleteEngineCache();

            // The second build should fetch and reuse the graph from the cache, even if the obj directory is cleaned
            RunEngine("Second build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            // Turn off the cache and ensure the graph is still retrieved from the copy in the object directory
            Configuration.Cache.Incremental = false;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            RunEngine("Third Build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache, count: 0);

            // Change an input and ensure the next build is invalidated
            AppendNewLine(Configuration.Startup.ConfigFile);

            RunEngine("Fourth Build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
        }

        [Fact]
        public void MiniBuildCachedCompressedGraph()
        {
            SetupMiniBuild();

            Configuration.Engine.CompressGraphFiles = true;
            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;

            RunEngine("First build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            DeleteEngineCache();

            RunEngine("Second build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);

            // Turn off the cache and ensure the graph is still retrieved from the copy in the object directory
            Configuration.Cache.Incremental = false;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            RunEngine("Third build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);

            // Turn off the compression and the graph needs to be constructed.
            Configuration.Engine.CompressGraphFiles = false;
            RunEngine("Fourth build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 1);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState, count: 0);

            // Change an input and ensure the next build is invalidated
            AppendNewLine(Configuration.Startup.ConfigFile);

            RunEngine("Fifth build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
        }


        [Fact]
        public void MiniBuildWithSpecCache()
        {
            SetupMiniBuild();

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;
            Configuration.Cache.CacheSpecs = SpecCachingOption.Enabled;

            RunEngine("First build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            // Change an input and ensure the next build is invalidated
            AppendNewLine(Configuration.Startup.ConfigFile);

            Configuration.FrontEnd.LogStatistics = true; // turn on stats logging to validate
            RunEngine("Second build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
        }

        [Fact]
        public void TestHistoricTableSizesInReloadedEngineContext()
        {
            SetupMiniBuild();
            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = false;
            Configuration.Cache.Incremental = true;
            Configuration.Cache.CacheSpecs = SpecCachingOption.Enabled;
            Configuration.FrontEnd.ReloadPartialEngineStateWhenPossible = true;

            RunEngine("First build");
            var capturedTableSizesForEngine1 = Context.NextHistoricTableSizes;

            AssertLogged(FrontEndEventId.FrontEndStartEvaluateValues); // specs were evaluated
            AssertNotLogged(LogEventId.StartDeserializingEngineState); // engine state wasn't reloaded
            AssertLogged(LogEventId.EndSerializingPipGraph); // graph was serialized

            var sourceRootPath = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);
            var mini1DsFile = Path.Combine(sourceRootPath, "mini1.dsc");

            // Change 1 input --> assert engine state was reloaded and new historic table sizes contain stats from the engine1 run
            AppendNewLine(mini1DsFile);
            RunEngine("Second build", rememberAllChangedTrackedInputs: true);
            var capturedTableSizesForEngine2 = Context.NextHistoricTableSizes;

            AssertLogged(FrontEndEventId.FrontEndStartEvaluateValues); // specs were evaluated
            AssertLogged(LogEventId.StartDeserializingEngineState); // engine state was reloaded
            AssertLogged(LogEventId.EndSerializingPipGraph); // graph was serialized
            XAssert.AreEqual(1, Context.HistoricTableSizes.Count);

            // Don't change anything --> assert full graph was reloaded and new historc table sizes contain stats from both engine runs
            RunEngine("Third build");
            AssertNotLogged(FrontEndEventId.FrontEndStartEvaluateValues); // specs were not evaluated
            AssertNotLogged(LogEventId.StartDeserializingEngineState); // engine state was not reloaded (because the whole graph was reloaded)
            AssertNotLogged(LogEventId.EndSerializingPipGraph); // graph was not serialized (because it was relaoded hence didn't change)
            XAssert.AreEqual(2, Context.HistoricTableSizes.Count);

            // Don't change anything --> assert full graph was reloaded and new historic table sizes still contain only the stats from the first two runs
            RunEngine("Fourth build");
            AssertNotLogged(FrontEndEventId.FrontEndStartEvaluateValues); // specs were not evaluated
            AssertNotLogged(LogEventId.StartDeserializingEngineState); // engine state was not reloaded (because the whole graph was reloaded)
            AssertNotLogged(LogEventId.EndSerializingPipGraph); // graph was not serialized (because it was relaoded hence didn't change)
            var historicData = Context.HistoricTableSizes;
            XAssert.AreEqual(2, historicData.Count);
            XAssert.IsTrue(historicData.Last().TotalSizeInBytes() < 2 * historicData.First().TotalSizeInBytes());

            // change 1 input, but conifgure engine not to partially reload state -> no historic table sizes are available
            AppendNewLine(mini1DsFile);
            Configuration.FrontEnd.ReloadPartialEngineStateWhenPossible = false;
            Configuration.FrontEnd.UseSpecPublicFacadeAndAstWhenAvailable = false; // Have to turn off this or else it will force partialreload to be true.
            RunEngine("Fifth build", rememberAllChangedTrackedInputs: true);
            AssertLogged(FrontEndEventId.FrontEndStartEvaluateValues); // specs were evaluated
            AssertNotLogged(LogEventId.StartDeserializingEngineState); // engine state was not reloaded
            AssertLogged(LogEventId.EndSerializingPipGraph); // graph was serialized
        }

        [Fact]
        public void TestPartialReload()
        {
            SetupMiniBuild();

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;
            Configuration.Cache.CacheSpecs = SpecCachingOption.Enabled;
            Configuration.FrontEnd.ReloadPartialEngineStateWhenPossible = true;

            RunEngine("First build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            var sourceRootPath = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);
            var mini1DsFile = Path.Combine(sourceRootPath, "mini1.dsc");
            var mini2DsFile = Path.Combine(sourceRootPath, "mini2.dsc");
            var mini3DsFile = Path.Combine(sourceRootPath, "mini3.dsc");

            // Change 1 input, 'mini1.ds'
            AppendNewLine(mini1DsFile);
            RunEngine("Second build", rememberAllChangedTrackedInputs: true, captureFrontEndAbstraction: true);
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertChangedFiles(new[] {mini1DsFile}, new[] {mini2DsFile, mini3DsFile});
            AssertInformationalEventLogged(LogEventId.StartDeserializingEngineState);
        }

        [Fact]
        public void MiniBuildInputChanges()
        {
            SetupMiniBuild();

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;
            Configuration.Cache.CacheSpecs = SpecCachingOption.Enabled;

            RunEngine("First build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            var sourceRootPath = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);
            var mini1DsFile = Path.Combine(sourceRootPath, "mini1.dsc");
            var mini2DsFile = Path.Combine(sourceRootPath, "mini2.dsc");
            var mini3DsFile = Path.Combine(sourceRootPath, "mini3.dsc");

            // Change 1 input, 'mini1.ds'
            AppendNewLine(mini1DsFile);
            RunEngine("Second build", rememberAllChangedTrackedInputs: true, captureFrontEndAbstraction: true);
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertChangedFiles(new[] {mini1DsFile}, new[] {mini2DsFile, mini3DsFile});

            // Change 2 inputs: 'mini2.ds' and 'mini3.ds'
            AppendNewLine(mini2DsFile);
            AppendNewLine(mini3DsFile);
            RunEngine("Third build", rememberAllChangedTrackedInputs: true, captureFrontEndAbstraction: true);
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertChangedFiles(new[] {mini2DsFile, mini3DsFile}, new[] {mini1DsFile});

            // Change 3 inputs: 'mini1.ds', 'mini2.ds' and 'mini3.ds'
            AppendNewLine(mini1DsFile);
            AppendNewLine(mini2DsFile);
            AppendNewLine(mini3DsFile);
            RunEngine("Fouth build", rememberAllChangedTrackedInputs: true, captureFrontEndAbstraction: true);
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertChangedFiles(new[] {mini1DsFile, mini2DsFile, mini3DsFile}, new string[] { });
        }

        [Fact]
        public void MiniBuildCachedGraphWithInputChanges()
        {
            SetupMiniBuild();

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;

            RunEngine("First build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            var sourceRootPath = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);

            var mini1DsFile = Path.Combine(sourceRootPath, "mini1.dsc");
            var copyMini1DsFile = Path.Combine(sourceRootPath, "mini1.dsc.copy");

            var mini2DsFile = Path.Combine(sourceRootPath, "mini2.dsc");
            var copyMini2DsFile = Path.Combine(sourceRootPath, "mini2.dsc.copy");

            var engineCacheDirectoryPath = Configuration.Layout.EngineCacheDirectory.ToString(Context.PathTable);
            DeleteEngineCache();

            // Modify spec and expect the graph to be re-constructed.
            File.Copy(mini1DsFile, copyMini1DsFile);
            AppendNewLine(mini1DsFile);
            RunEngine("Second build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            DeleteEngineCache();

            // Write again the original spec and expect to get graph from cache.
            File.Copy(copyMini1DsFile, mini1DsFile, overwrite: true);
            RunEngine("Third build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            DeleteEngineCache();

            // Write again the modified spec and expect to get graph from cache.
            AppendNewLine(mini1DsFile);
            RunEngine("Fourth build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            DeleteEngineCache();

            // Modify another spec and expect the graph to be re-constructed.
            File.Copy(mini2DsFile, copyMini2DsFile);
            AppendNewLine(mini2DsFile);
            RunEngine("Fifth build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            DeleteEngineCache();

            // Restore spec and expect to get graph from cache.
            File.Copy(copyMini2DsFile, mini2DsFile, overwrite: true);
            RunEngine("Sixth build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            DeleteEngineCache();

            // Modify engine and expect the graph to be re-constructed, and no conflict in storing the graph.

            TestHooks.GraphFingerprintSalt = 42;
            RunEngine("Seventh build");
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");
        }

        /// <summary>
        /// Test for Bug #1276630
        /// </summary>
        [FactIfSupported(requiresJournalScan: true)]
        public void MiniBuildInputTrackerAndFileChangeTrackerInSyncWithThePresenceOfCachedGraphs()
        {
            SetupMiniBuild();

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;

            var sourceRootPath = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);
            var engineCacheDirectoryPath = Configuration.Layout.EngineCacheDirectory.ToString(Context.PathTable);
            var miniDsFile = Path.Combine(sourceRootPath, "mini1.dsc");
            var miniV1DsFile = Path.Combine(sourceRootPath, "mini1.dsc.v1");
            var miniV2DsFile = Path.Combine(sourceRootPath, "mini1.dsc.v2");
            File.Copy(miniDsFile, miniV1DsFile);
            File.Copy(miniDsFile, miniV2DsFile);

            // Modify mini ds file to create V2.
            AppendNewLine(miniV2DsFile);

            void restoreMiniDs(string version)
            {
                FileUtilities.DeleteFile(miniDsFile);
                XAssert.AreEqual(CreateHardLinkStatus.Success, FileUtilities.TryCreateHardLink(miniDsFile, version));
            }

            // Restore V1.
            restoreMiniDs(miniV1DsFile);
            RunEngine("Restore v1");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            // Restore V2.
            restoreMiniDs(miniV2DsFile);
            RunEngine("Restore v2", rememberAllChangedTrackedInputs: true);

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            // Restore V1.
            restoreMiniDs(miniV1DsFile);
            RunEngine("Restore v1 second time", rememberAllChangedTrackedInputs: true);

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph, count: 0);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            // Re-run with V1.
            RunEngine("Runrun v1");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph, count: 0);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache, count: 0);

            // Restore V2
            restoreMiniDs(miniV2DsFile);
            RunEngine("restore v2", rememberAllChangedTrackedInputs: true);

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph, count: 0);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            // Restore V1
            restoreMiniDs(miniV1DsFile);
            RunEngine("restore v1", rememberAllChangedTrackedInputs: true);

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph, count: 0);
            // The bug caused the previous graph to be reused, instead of fetching from the cache.
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            DeleteEngineCache();

            // Run V1.
            RunEngine("run v1");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph, count: 0);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            // Re-run with V1.
            RunEngine("re-run v1");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph, count: 0);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache, count: 0);
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void MiniBuildCopySymlink(bool lazySymlinkCreation)
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                const string SymlinkSource1 = "symlink1.lnk";
                const string SymlinkSource2 = "symlink2.lnk";

                const string CopiedSymlink1 = "copied-" + SymlinkSource1;
                const string CopiedSymlink2 = "copied-" + SymlinkSource2;

                const string SymlinkTarget1 = "target1.txt";
                const string SymlinkTarget2 = "target2.txt";

                SetupCopySymlink(SymlinkSource1, CopiedSymlink1, SymlinkSource2, CopiedSymlink2);

                Configuration.Schedule.UnsafeLazySymlinkCreation = lazySymlinkCreation;
                Configuration.Cache.CacheGraph = true;

                var sourceDirectory = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);

                // Write symlink targets.
                File.WriteAllText(Path.Combine(sourceDirectory, SymlinkTarget1), "1");
                File.WriteAllText(Path.Combine(sourceDirectory, SymlinkTarget2), "2");

                // Write symlink definition file.
                var symlinkDefinitionFile = Path.Combine(sourceDirectory, "SymlinkDefinition");
                var pathMapSerializer = new PathMapSerializer(symlinkDefinitionFile);
                pathMapSerializer.OnNext(
                    new KeyValuePair<string, string>(Path.Combine(sourceDirectory, SymlinkSource1), Path.Combine(sourceDirectory, SymlinkTarget1)));
                pathMapSerializer.OnNext(
                    new KeyValuePair<string, string>(Path.Combine(sourceDirectory, SymlinkSource2), Path.Combine(sourceDirectory, SymlinkTarget2)));
                ((IObserver<KeyValuePair<string, string>>)pathMapSerializer).OnCompleted();

                // Allow copying symlink for testing, and set the symlink definition file.
                Configuration.Schedule.AllowCopySymlink = true;
                Configuration.Layout.SymlinkDefinitionFile = AbsolutePath.Create(Context.PathTable, symlinkDefinitionFile);
                IgnoreWarnings();
                RunEngine();

                var objectDirectoryPath = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);

                XAssert.IsTrue(File.Exists(Path.Combine(objectDirectoryPath, CopiedSymlink1)));
                XAssert.IsTrue(File.Exists(Path.Combine(objectDirectoryPath, CopiedSymlink2)));

                string content1 = File.ReadAllText(Path.Combine(objectDirectoryPath, CopiedSymlink1));
                string content2 = File.ReadAllText(Path.Combine(objectDirectoryPath, CopiedSymlink2));

                XAssert.AreEqual("1", content1);
                XAssert.AreEqual("2", content2);
            }
        }

        [Fact]
        public void MiniBuildCachedGraphWithAndWithoutEnvironmentAccess()
        {
            const string MountName = "MyMountTest";
            const string EnvVarName = "MyEnvTest";
            const string ModuleName = nameof(MiniBuildCachedGraphWithAndWithoutEnvironmentAccess);

            var mountPath = Path.Combine(Configuration.Layout.SourceDirectory.ToString(Context.PathTable), "Src");

            // Setup cache
            ConfigureInMemoryCache(new TestCache());
            SetConfigForPipsWithMountAndEnvironmentAccess(MountName, mountPath, ModuleName);
            SetupPipsWithOrWithoutEnvironmentAccess(ModuleName, EnvVarName, accessEnvironmentVariable: true);

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;
            Configuration.Startup.Properties.Add(EnvVarName, "MyEnvValue");

            RunEngine("First build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            DeleteEngineCache();

            SetupPipsWithOrWithoutEnvironmentAccess(ModuleName, EnvVarName, accessEnvironmentVariable: false);

            RunEngine("Second build -- with modified spec that no longer access environment variable");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            DeleteEngineCache();

            RunEngine("Third build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            DeleteEngineCache();

            // Although, the environment variable is no longer access, it is still used on
            // following the chain of cache look-ups. Thus, we will expect graph cache miss. 
            // This limitation is expected in graph caching algorithm, and is considered rare.
            Configuration.Startup.Properties[EnvVarName] = "MyEnvValue-Modfified";

            RunEngine("Forth build -- with modified value of unused environment variable");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            DeleteEngineCache();
        }

        [Fact]
        public void MiniBuildCachedGraphWithMountAndEnvironmentChanges()
        {
            const string MountName = "MyMountTest";
            const string EnvVarName = "MyEnvTest";
            const string SourceDir1 = "Src1";
            const string SourceDir2 = "Src2";
            const string ModuleName = nameof(MiniBuildCachedGraphWithMountAndEnvironmentChanges);

            var sourceRootPath = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);

            // Set layout for mounts.
            var sourceDir1 = Path.Combine(sourceRootPath, SourceDir1);
            var sourceDir2 = Path.Combine(sourceRootPath, SourceDir2);

            Directory.CreateDirectory(sourceDir1);
            Directory.CreateDirectory(sourceDir2);
            File.WriteAllText(Path.Combine(sourceDir1, "file.txt"), "Test1");
            File.WriteAllText(Path.Combine(sourceDir2, "file.txt"), "Test2");

            // Setup cache
            ConfigureInMemoryCache(new TestCache());

            // Setup spec and configuration.
            SetupPipsWithMountAndEnvironmentAccess(MountName, EnvVarName, ModuleName);
            SetConfigForPipsWithMountAndEnvironmentAccess(MountName, I($"./{SourceDir1}"), ModuleName);

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;
            Configuration.Startup.Properties.Add(EnvVarName, "Env1");

            RunEngine("First build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            DeleteEngineCache();

            // Modify environment variable.
            Configuration.Startup.Properties[EnvVarName] = "Env2";

            RunEngine("Second build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            DeleteEngineCache();

            // Revert environment variable.
            Configuration.Startup.Properties[EnvVarName] = "Env1";

            RunEngine("Third build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            DeleteEngineCache();

            // Change mounts by modifying config file.
            var configPath = Path.Combine(TestRoot, "config.dsc");
            var configCopyPath = Path.Combine(TestRoot, "config.dsc.copy");
            File.Copy(configPath, configCopyPath, overwrite: true);

            SetConfigForPipsWithMountAndEnvironmentAccess(MountName, I($"./{SourceDir2}"), ModuleName);

            RunEngine("Forth build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            DeleteEngineCache();

            // Revert config change.
            File.Copy(configCopyPath, configPath, overwrite: true);

            RunEngine("Fifth build");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);

            DeleteEngineCache();
        }

        /// <summary>
        /// This test shows the limitation of graph caching algorithm when a spec is no longer referenced
        /// during the build but gets modified.
        /// </summary>
        [Fact]
        public void MiniBuildCachedGraphWithMultipleSpecs()
        {
            const string ModuleName = nameof(MiniBuildCachedGraphWithMultipleSpecs);

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;

            SetupMiniBuildWithSingleModuleButMultipleSpecs(ModuleName, shouldPlaceInRoot: true, specCount: 2);

            RunEngine("First build -- run with 2 specs, spec1.dsc and spec2.dsc");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            DeleteEngineCache();

            SetupMiniBuildWithSingleModuleButMultipleSpecs(ModuleName, shouldPlaceInRoot: true, specCount: 1);

            RunEngine("Second build -- run with 1 spec, spec1.dsc");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            DeleteEngineCache();

            var sourceRootPath = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);
            var spec2Dsc = Path.Combine(sourceRootPath, "spec2.dsc");
            File.AppendAllText(spec2Dsc, "// Adding a comment");

            RunEngine("Third build -- run with 1 spec, spec1.dsc, but modified non-referenced spec2.dsc");

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            DeleteEngineCache();
        }

        [Fact]
        public void MiniBuildWithMultipleModules()
        {
            string moduleName1 = nameof(MiniBuildWithMultipleModules) + "1";
            string moduleName2 = nameof(MiniBuildWithMultipleModules) + "2";

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;

            SetupMiniBuildWithMultipleModules(moduleName1, moduleName2);

            // Build module 1 and module 2.
            Configuration.Filter = I($"(module='{moduleName1}') or (module='{moduleName2}')");
            RunEngine(I($"First build -- build {moduleName1}, {moduleName2}"));

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            // Build only module 2.
            Configuration.Filter = I($"(module='{moduleName2}')");
            RunEngine(I($"Second build -- build {moduleName2}"));

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph, count: 0);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MiniBuildCachedGraphWithInputProbesBehavior(bool testAbsentProbe)
        {
            AddModule("MiniBuild", new[] { ("mini1.dsc", "const x = File.exists(f`test.txt`);") }, placeInRoot: true);
            ConfigureInMemoryCache(new TestCache());

            // Create test.txt
            var sourceRootPath = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);
            var testTxt = Path.Combine(sourceRootPath, "test.txt");

            if (testAbsentProbe)
            {
                // Testing absent probe. Make sure the file is not there, just being defensive.
                File.Delete(testTxt);
            }
            else
            {
                // Testing present probe. Create the file.
                File.WriteAllText(testTxt, "Hello");
            }

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = true;
            Configuration.Cache.Incremental = true;

            RunEngine("First build", rememberAllChangedTrackedInputs: true);

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            if (testAbsentProbe)
            {
                // In the first build the probe was absent. Let's make it present.
                File.WriteAllText(testTxt, "Hello");
            }
            else
            {
                // In the first build the probe was present. Let's make it absent.
                File.Delete(testTxt);
            }

            RunEngine("Second build", rememberAllChangedTrackedInputs: true);

            // In any case, the graph should be rebuilt
            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
        }

        [Theory]
        [InlineData("const x = File.exists(f`test.txt`);", false, true)]
        [InlineData("const x = File.exists(f`test.txt`); const y = File.readAllText(f`test.txt`);", true, true)]
        [InlineData("const y = File.readAllText(f`test.txt`); const x = File.exists(f`test.txt`);", true, true)]
        [InlineData("const x = File.exists(f`test.txt`);", false, false)]
        [InlineData("const x = File.exists(f`test.txt`); const y = File.readAllText(f`test.txt`);", true, false)]
        [InlineData("const y = File.readAllText(f`test.txt`); const x = File.exists(f`test.txt`);", true, false)]
        public void MiniBuildCachedGraphWithInputProbesBehaviorOnContentModification(string specContent, bool expectGraphToRebuild, bool retrieveContentFromCache)
        {
            AddModule("MiniBuild", new[] { ("mini1.dsc", specContent) }, placeInRoot: true);
            ConfigureInMemoryCache(new TestCache());

            // Create test.txt
            var sourceRootPath = Configuration.Layout.SourceDirectory.ToString(Context.PathTable);
            var testTxt = Path.Combine(sourceRootPath, "test.txt");
            File.WriteAllText(testTxt, "Hello");

            Configuration.Cache.CacheGraph = true;
            Configuration.Cache.AllowFetchingCachedGraphFromContentCache = retrieveContentFromCache;
            Configuration.Cache.Incremental = true;

            RunEngine("First build", rememberAllChangedTrackedInputs: true);

            AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);

            // If the potential matching content should come from the cache, let's force it by
            // deleting the engine cache
            if (retrieveContentFromCache)
            {
                DeleteEngineCache();
            }

            // Modify the file. Probing should not be affected unless the file is also read.
            AppendNewLine(testTxt);

            RunEngine("Second build", rememberAllChangedTrackedInputs: true);

            if (expectGraphToRebuild)
            {
                AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues);
                AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            }
            else
            {
                AssertInformationalEventLogged(FrontEndEventId.FrontEndStartEvaluateValues, count: 0);
                AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
                if (retrieveContentFromCache)
                {
                    AssertInformationalEventLogged(LogEventId.FetchedSerializedGraphFromCache);
                }
            }
        }

        private void DeleteEngineCache()
        {
            // Clean the Engine Cache
            var engineCacheDirectoryPath = Configuration.Layout.EngineCacheDirectory.ToString(Context.PathTable);
            FileUtilities.DeleteDirectoryContents(engineCacheDirectoryPath);
        }

        private void AssertLogged(Enum eventId) => AssertInformationalEventLogged(eventId, count: 1);

        private void AssertNotLogged(Enum eventId) => AssertInformationalEventLogged(eventId, count: 0);

        private void AssertChangedFiles(string[] changedFiles, string[] containsUnchangedFiles)
        {
            var engineAbstraction = TestHooks.FrontEndEngineAbstraction.Value;
            XAssert.IsNotNull(engineAbstraction.GetChangedFiles());
            XAssert.IsNotNull(engineAbstraction.GetUnchangedFiles());
            XAssert.SetEqual(changedFiles, engineAbstraction.GetChangedFiles());
            XAssert.All(
                containsUnchangedFiles,
                unchangedFile => { XAssert.IsTrue(engineAbstraction.GetUnchangedFiles().Contains(unchangedFile)); });
        }

        private void SetupMiniBuild()
        {
            var mini1Spec = @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';

export const mini1 = Transformer.writeAllLines(
    p`obj/mini1.txt`,
    [
        'mini1-line1',
        'mini1-line2',
    ]
);
";

            var mini2Spec = @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';

export const mini2 = Transformer.writeAllLines(
    p`obj/mini2.txt`,
    [
        'mini2-line1',
        'mini2-line2',
    ]
);
";

            var mini3Spec = @"
import * as Csc from 'Sdk.Managed.Tools.Csc';

export const mini3 = Csc.compile({
     outputPath: p`obj/mini3.exe`,
     targetType: 'exe',
     sources: [
         f`mini3.cs`,
     ],
});
";

            AddModule(
                "MiniBuild",
                new[]
                {
                    ("mini1.dsc", mini1Spec),
                    ("mini2.dsc", mini2Spec),
                    ("mini3.dsc", mini3Spec),
                },
                placeInRoot: true);
            AddFile("mini3.cs", @"class Program { static void Main() {} }");
            AddCscDeployemnt();

            ConfigureInMemoryCache(new TestCache());
        }

        private void SetupHelloWorld()
        {
            AddCscDeployemnt();

            var spec = @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';
import * as Deployment from 'Sdk.Deployment';
import * as Csc from 'Sdk.Managed.Tools.Csc';

const resultFolder = d`${Context.getMount('ObjectRoot').path}`;

const helloWorldCs : File = Transformer.writeAllLines(
    p`${resultFolder}/HelloWorld.cs`,
    [
        'using System;',
        'using System.IO;',
        'namespace HelloWorld',
        '{',
        '  class Program',
        '  {',
        '    static void Main(string[] args)',
        '    {',
        '      var msg = ""Hello World, BuildXL!"";',
        '      Console.WriteLine(msg);',
        '      File.WriteAllText(args[0], msg);',
        '    }',
        '  }',
        '}',
    ]
);

const helloWorldExe : File = Csc.compile({
    outputPath: p`${resultFolder}/HelloWorld.exe`,
    targetType: 'exe',
    sources: [
        helloWorldCs,
    ],
}).binary;

const helloWorldExeConfig : File = Transformer.writeAllLines(
    p`${resultFolder}/HelloWorld.exe.config`,
    [
        ""<?xml version='1.0' encoding='utf-8'?>"",
        ""    <configuration>"",
        ""</configuration>"",
    ]
);

const helloWorldToolDefinition = {
    exe: helloWorldExe,
    description: 'HelloWorld tool',
    runtimeDependencies: [
        helloWorldExeConfig,
    ],
    dependsOnWindowsDirectories: true,
};

const helloWorldResultOutputLoction = p`${resultFolder}/HelloWorld.out`;
const helloWorldResult = Transformer.execute({
    tool: helloWorldToolDefinition,
    workingDirectory: resultFolder,
    arguments: [
        Cmd.argument(Artifact.output(helloWorldResultOutputLoction)),
    ],
}).getOutputFile(helloWorldResultOutputLoction);

const deployedFiles = Deployment.deployToDisk({
    targetDirectory: resultFolder,
    definition: {
        contents: [
            {
                subfolder: a`src`,
                contents: [
                    helloWorldCs,
                ],
            },
            {
                subfolder: a`bin`,
                contents: [
                    helloWorldExeConfig,
                    helloWorldExe,
                ]
            }
        ],
    },
});

const helloWorldCpp : File = Transformer.writeAllLines(
    p`${resultFolder}/HelloWorld.cpp`,
    [
        'int main(char *argc, int argv) {{ return 0; }}',
    ]
);

";
            AddModule("HelloWorld", ("hello.dsc", spec));
        }

        // TODO: This code refers to the installed csc.exe, this does not work on CoreClr
        // We need to do the work to pick this up from the nuget package.
        private void AddCscDeployemnt()
        {
            // Ignore DX222 for csc.exe being outside of src directory
            IgnoreWarnings();
            AddSdk(GetTestDataValue("MicrosoftNetCompilersSdkLocation"));

            var spec = @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';

const pkgContents = importFrom('Microsoft.Net.Compilers').Contents.all;

@@public
export const tool = {
    exe: pkgContents.getFile(r`tools/csc.exe`),
    description: 'Microsoft C# Compiler',
    runtimeDependencies: pkgContents.contents,
    untrackedDirectoryScopes: [
        d`${Context.getMount('ProgramData').path}`,
    ],
    dependsOnWindowsDirectories: true,
    prepareTempDirectory: true,
};

@@public
export interface Arguments {
    outputPath: Path;
    targetType?: 'library' | 'exe';
    sources: File[];
}

@@public
export interface Result {
    binary: File
}

@@public
export function compile(args: Arguments) : Result {
    let result = Transformer.execute({
        tool: tool,
        workingDirectory: d`.`,
        arguments: [
            Cmd.option('/out:',               Artifact.output(args.outputPath)),
            Cmd.option('/target:',            args.targetType),
            Cmd.files(args.sources),
        ],
    });

    return {
        binary: result.getOutputFile(args.outputPath),
    };
}
";
            AddModule("Sdk.Managed.Tools.Csc", ("cscHack.dsc", spec));
        }

        private void SetupCopySymlink(string symlinkSourceName1, string copiedSymlinkName1, string symlinkSourceName2, string copiedSymlinkName2)
        {

            var spec = I(
                $@"
import {{Artifact, Cmd, Tool, Transformer}} from 'Sdk.Transformers';

export const copySymlink1 = Transformer.copyFile(f`{symlinkSourceName1}`, p`obj/{copiedSymlinkName1}`);
export const copySymlink2 = Transformer.copyFile(f`{symlinkSourceName2}`, p`obj/{copiedSymlinkName2}`);
");

            AddModule("SymLinkTest", ("symlinktest.dsc", spec), placeInRoot: true);
        }

        private void SetupPipsWithOrWithoutEnvironmentAccess(string moduleName, string environmentVarName, bool accessEnvironmentVariable)
        {
            var writtenText = accessEnvironmentVariable 
                ? $"Environment.getStringValue('{environmentVarName}')" 
                : $"'{nameof(SetupPipsWithOrWithoutEnvironmentAccess)}'";

            var spec = I(
                $@"
import {{Transformer}} from 'Sdk.Transformers';

const outputDirectory = Context.getNewOutputDirectory('{moduleName}');
export const writeFile = Transformer.writeAllLines(p`${{outputDirectory}}/write.out`, [{writtenText}]);
");
            AddModule(moduleName, ("spec.dsc", spec));
        }

        private void SetupPipsWithMountAndEnvironmentAccess(string mountName, string environmentVarName, string moduleName)
        {
            var spec = I(
                $@"
import {{Transformer}} from 'Sdk.Transformers';

const testNonExistentMount = Context.hasMount('NonExistentMount');
const outputDirectory = Context.getNewOutputDirectory('{moduleName}');
const sourcePath = Context.getMount('{mountName}').path;
const logsPath = Context.getMount('LogsDirectory').path;
export const copyFile  = Transformer.copyFile(f`${{sourcePath}}/file.txt`, p`${{outputDirectory}}/copy.out`);
export const writeFile = Transformer.writeAllLines(p`${{outputDirectory}}/write.out`, [Environment.getStringValue('{environmentVarName}')]);
");

            AddModule(moduleName, ("spec.dsc", spec));
        }

        private void SetConfigForPipsWithMountAndEnvironmentAccess(string mountName, string mountPath, string moduleName)
        {
            SetConfig(I(
               $@"
config({{
    modules: [f`./{moduleName}/module.config.dsc`],
    mounts: [
        {{
            name: a`{mountName}`,
            path: p`{mountPath}`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true
        }},
     ]
}});"));
        }

        private void SetupMiniBuildWithMultipleModules(params string[] moduleNames)
        {
            foreach (var moduleName in moduleNames)
            {
                var spec = I(
                $@"
import {{Transformer}} from 'Sdk.Transformers';

const outputDirectory = Context.getNewOutputDirectory('{moduleName}');

// Write module name into write.out.
export const writeFileFor{moduleName} = Transformer.writeAllLines(p`${{outputDirectory}}/write.out`, [""{moduleName}""]);
");

                AddModule(moduleName, ("spec.dsc", spec));
            }

            string moduleConfigFiles = string.Join(", ", moduleNames.Select(m => I($"f`./{m}/module.config.dsc`")));
            SetConfig(I(
               $@"
config({{ 
    modules: [{moduleConfigFiles}] 
}});"));
        }

        private void SetupMiniBuildWithSingleModuleButMultipleSpecs(string moduleName, bool shouldPlaceInRoot, int specCount)
        {
            for (int i = 1; i <= specCount; ++i)
            {
                var spec = I(
                                $@"
import {{Transformer}} from 'Sdk.Transformers';

const outputDirectory = Context.getNewOutputDirectory('{moduleName}');
export const writeFileFor{moduleName}_{i} = Transformer.writeAllLines(p`${{outputDirectory}}/write.out`, [""{moduleName}_{i}""]);
");

                AddModule(moduleName, ($"spec{i}.dsc", spec), placeInRoot: shouldPlaceInRoot);
            }
        }
    }
}
