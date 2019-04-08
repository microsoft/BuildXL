// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

// ReSharper disable ConvertClosureToMethodGroup
namespace ContentStoreTest.Synchronization
{
    public abstract class SingleInstanceTests : TestBase
    {
        protected SingleInstanceTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger)
            : base(createFileSystemFunc, logger)
        {
        }

        protected abstract IStartupShutdown CreateFirstInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds);

        protected abstract IStartupShutdown CreateSecondInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds);

        protected virtual string TimeoutErrorMessageFragment => string.Empty;

        [Fact]
        public void SecondTimesOutWhenFirstWins()
        {
            LoserTimesOut(
                (testDir, timeout) => CreateFirstInstance(testDir, timeout),
                (testDir, timeout) => CreateSecondInstance(testDir, timeout));
        }

        [Fact]
        public void FirstTimesOutWhenSecondWins()
        {
            LoserTimesOut(
                (testDir, timeout) => CreateSecondInstance(testDir, timeout),
                (testDir, timeout) => CreateFirstInstance(testDir, timeout));
        }

        private void LoserTimesOut(
            Func<DisposableDirectory, int, IStartupShutdown> winnerFunc, Func<DisposableDirectory, int, IStartupShutdown> loserFunc)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                using (var winner = winnerFunc(testDirectory, 1))
                {
                    winner.StartupAsync(context).GetAwaiter().GetResult().ShouldBeSuccess();
                    using (var loser = loserFunc(testDirectory, 1))
                    {
                        loser.StartupAsync(context).GetAwaiter().GetResult().ShouldBeError(TimeoutErrorMessageFragment);
                    }

                    winner.ShutdownAsync(context).GetAwaiter().GetResult().ShouldBeSuccess();
                }
            }
        }

        [Fact(Skip = "TODO: Failing locally during conversion")]
        [Trait("Category", "QTestSkip")] // Skipped
        public async Task SequentialAccess()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                const int singleInstanceTimeoutSeconds = 10;

                // Create two stores against the same root
                var caches = new[]
                {
                    CreateFirstInstance(testDirectory, singleInstanceTimeoutSeconds),
                    CreateSecondInstance(testDirectory, singleInstanceTimeoutSeconds)
                };

                // Startup the two caches in parallel
                var startupTasks = caches.Select(cache => cache.StartupAsync(context)).ToList();

                // One cache will win the race
                var winningTask = await Task.WhenAny(startupTasks);
                var winningCacheIndices = new[] { 0, 1 }.Where(i => startupTasks[i] == winningTask).ToList();
                winningCacheIndices.Count.Should().Be(1);
                var winningCacheIndex = winningCacheIndices.Single();
                (await caches[winningCacheIndex].ShutdownAsync(context)).ShouldBeSuccess();
                caches[winningCacheIndex].Dispose();

                // The losing cache should be able to finish starting up after the winner is disposed
                var losingCacheIndex = (winningCacheIndex + 1) % 2;
                (await startupTasks[losingCacheIndex]).ShouldBeSuccess();
                (await caches[losingCacheIndex].ShutdownAsync(context)).ShouldBeSuccess();
                caches[losingCacheIndex].Dispose();
            }
        }
    }
}
