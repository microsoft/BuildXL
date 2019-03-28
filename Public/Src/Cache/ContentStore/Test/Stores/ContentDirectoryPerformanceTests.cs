// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Performance;
using ContentStoreTest.Test;
using Xunit;

#pragma warning disable SA1402 // File may only contain a single class

namespace ContentStoreTest.Performance.Stores
{
    public abstract class ContentDirectoryPerformanceTests : TestBase
    {
        private readonly IClock _clock = new MemoryClock();
        private readonly PerformanceResultsFixture _resultsFixture;

        protected ContentDirectoryPerformanceTests(ILogger logger, PerformanceResultsFixture resultsFixture)
            : base(() => new PassThroughFileSystem(logger), logger)
        {
            _resultsFixture = resultsFixture;
        }

        protected abstract IContentDirectory Create(AbsolutePath directoryPath);

        [Fact]
        public async Task Startup()
        {
            const int count = 5;
            var context = new Context(Logger);
            var times = new List<TimeSpan>(count);

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                // Establish an existing serialized file.
                await Run(context, testDirectory.Path, PopulateRandom);

                for (var i = 0; i < count; i++)
                {
                    using (var directory = Create(testDirectory.Path))
                    {
                        try
                        {
                            var stopwatch = Stopwatch.StartNew();
                            var r = await directory.StartupAsync(context);
                            stopwatch.Stop();
                            times.Add(stopwatch.Elapsed);
                            r.ShouldBeSuccess();
                        }
                        finally
                        {
                            var r = await directory.ShutdownAsync(context);
                            r.ShouldBeSuccess();
                        }
                    }
                }
            }

            double totalTime = times.Sum(x => x.TotalMilliseconds);
            double averageTime = totalTime / count;
            var name = GetType().Name + "." + nameof(Startup);
            _resultsFixture.AddResults(Output, name, (long)averageTime, "milliseconds", count);
        }

        [Fact]
        public async Task Shutdown()
        {
            const int count = 5;
            var context = new Context(Logger);
            var times = new List<TimeSpan>(count);

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                // Establish an existing serialized file.
                await Run(context, testDirectory.Path, PopulateRandom);

                for (var i = 0; i < count; i++)
                {
                    using (var directory = Create(testDirectory.Path))
                    {
                        try
                        {
                            var r = await directory.StartupAsync(context);
                            r.ShouldBeSuccess();
                        }
                        finally
                        {
                            var stopwatch = Stopwatch.StartNew();
                            var r = await directory.ShutdownAsync(context);
                            stopwatch.Stop();
                            times.Add(stopwatch.Elapsed);
                            r.ShouldBeSuccess();
                        }
                    }
                }
            }

            double totalTime = times.Sum(x => x.TotalMilliseconds);
            double averageTime = totalTime / count;
            var name = GetType().Name + "." + nameof(Shutdown);
            _resultsFixture.AddResults(Output, name, (long)averageTime, "milliseconds", count);
        }

        private async Task Run(Context context, AbsolutePath directoryPath, Func<IContentDirectory, Task> funcAsync)
        {
            using (var directory = Create(directoryPath))
            {
                try
                {
                    var r = await directory.StartupAsync(context);
                    r.ShouldBeSuccess();

                    if (funcAsync != null)
                    {
                        await funcAsync(directory);
                    }
                }
                finally
                {
                    var r = await directory.ShutdownAsync(context);
                    r.ShouldBeSuccess();
                }
            }
        }

        private async Task PopulateRandom(IContentDirectory directory)
        {
            for (var i = 0; i < 500000; i++)
            {
                var contentHash = ContentHash.Random();

                await directory.UpdateAsync(contentHash, false, _clock, fileInfo =>
                    Task.FromResult(new ContentFileInfo(_clock, 0, 7)));
            }
        }
    }

    [Trait("Category", "Performance")]
    public class MemoryContentDirectoryPerformanceTests : ContentDirectoryPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public MemoryContentDirectoryPerformanceTests(PerformanceResultsFixture resultsFixture)
            : base(TestGlobal.Logger, resultsFixture)
        {
        }

        protected override IContentDirectory Create(AbsolutePath directoryPath)
        {
            return new MemoryContentDirectory(FileSystem, directoryPath);
        }
    }
}
