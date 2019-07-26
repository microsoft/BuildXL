// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public sealed class LocalCacheWithStreamPathTwoCasTests : LocalCacheTests
    {
        private const string RootOfContentStoreForStream = "streams";
        private const string RootOfContentStoreForPath = "paths";

        private static AbsolutePath RootPathOfContentStoreForStream(DisposableDirectory testDirectory)
        {
            return testDirectory.Path / RootOfContentStoreForStream;
        }

        private static AbsolutePath RootPathOfContentStoreForPath(DisposableDirectory testDirectory)
        {
            return testDirectory.Path / RootOfContentStoreForPath;
        }

        private struct StreamPathCasStats
        {
            public long? StreamCasValue;
            public long? PathCasValue;
        }

        protected override ICache CreateCache(DisposableDirectory testDirectory)
        {
            var rootPathForStream = RootPathOfContentStoreForStream(testDirectory);
            var rootPathForPath = RootPathOfContentStoreForPath(testDirectory);

            FileSystem.CreateDirectory(rootPathForStream);
            FileSystem.CreateDirectory(rootPathForPath);

            var configuration1 = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
            var configuration2 = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);

            configuration1.Write(FileSystem, rootPathForStream).Wait();
            configuration2.Write(FileSystem, rootPathForPath).Wait();

            var memoConfig = new SQLiteMemoizationStoreConfiguration(rootPathForPath) { MaxRowCount = MaxContentHashListItems };
            memoConfig.Database.JournalMode = ContentStore.SQLite.JournalMode.OFF;
            return new LocalCache(Logger, rootPathForStream, rootPathForPath, memoConfig, clock: Clock);
        }

        [Fact]
        protected override async Task ContentStoreStartupFails()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                using (var cache = CreateCache(testDirectory))
                {
                    // To cause content store startup to fail, create a file with the same name as an expected directory.
                    // We just create for path CAS.
                    FileSystem.WriteAllBytes(RootPathOfContentStoreForPath(testDirectory) / "Shared", new byte[] {0});

                    var r = await cache.StartupAsync(context);
                    r.ShouldBeError("Content store startup failed");
                }
            }
        }

        [Fact]
        protected override async Task MemoizationStoreStartupFails()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                using (var cache = CreateCache(testDirectory))
                {
                    // To cause memoization store startup to fail, create a directory with the same name as the expected database file.
                    var databaseFilePath = RootPathOfContentStoreForPath(testDirectory) / SQLiteMemoizationStore.DefaultDatabaseFileName;
                    FileSystem.CreateDirectory(databaseFilePath);

                    var r = await cache.StartupAsync(context);
                    r.ShouldBeError("Memoization store startup failed");
                }
            }
        }

        protected override async Task VerifyPinCallCounterBumpedOnUse(ICache cache, Context context)
        {
            var values = await GetCounterValues("PinCall", cache, context);
            values.StreamCasValue.Should().Be(1);
            values.PathCasValue.Should().Be(1);
        }

        protected override async Task VerifyOpenStreamCallCounterBumpedOnUse(ICache cache, Context context)
        {
            var values = await GetCounterValues("OpenStreamCall", cache, context);
            values.StreamCasValue.Should().Be(1);
            values.PathCasValue.Should().Be(1);
        }

        protected override async Task VerifyPlaceFileCallCounterBumpedOnUse(ICache cache, Context context)
        {
            var values = await GetCounterValues("PlaceFileCall", cache, context);
            values.StreamCasValue.Should().Be(1);
            values.PathCasValue.Should().Be(1);
        }

        protected override async Task VerifyPutFileCallCounterBumpedOnUse(ICache cache, Context context)
        {
            var values = await GetCounterValues("PutFileCall", cache, context);
            values.StreamCasValue.Should().Be(0);
            values.PathCasValue.Should().Be(1);
        }

        protected override async Task VerifyPutStreamCallCounterBumpedOnUse(ICache cache, Context context)
        {
            var values = await GetCounterValues("PutStreamCall", cache, context);
            values.StreamCasValue.Should().Be(1);
            values.PathCasValue.Should().Be(0);
        }

        private async Task<StreamPathCasStats> GetCounterValues(string name, ICache cache, Context context)
        {
            var statsResult = await GetStatsResult(cache, context);
            var counters = statsResult.CounterSet.ToDictionaryIntegral();
            var countersForStreamCas =
                counters.Keys.Where(k => k.StartsWith(StreamPathContentStore.NameOfContentStoreForStream + "."))
                    .ToDictionary(key => key, key => counters[key]);
            var countersForPathCas =
                counters.Keys.Where(k => k.StartsWith(StreamPathContentStore.NameOfContentStoreForPath + "."))
                    .ToDictionary(key => key, key => counters[key]);
            return new StreamPathCasStats
            {
                PathCasValue = GetValue(name, countersForPathCas),
                StreamCasValue = GetValue(name, countersForStreamCas)
            };
        }

        private static long? GetValue(string name, Dictionary<string, long> counters)
        {
            long? value = null;

            var match = counters.Keys.FirstOrDefault(key => key.Contains(name));
            if (match != null)
            {
                value = counters[match];
            }

            return value;
        }
    }
}
