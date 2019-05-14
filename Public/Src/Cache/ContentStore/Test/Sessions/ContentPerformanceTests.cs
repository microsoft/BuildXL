// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Performance;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;

namespace ContentStoreTest.Performance.Sessions
{
    public abstract class ContentPerformanceTests : TestBase
    {
        private const string CacheName = "test";
        private const long MaxSizeInMegabytes = 5 * 1024;
        private const long MaxSize = MaxSizeInMegabytes * 1024 * 1024;
        private const string ItemCountEnvironmentVariableName = "ContentPerformanceTestsItemCount";
        private const int ItemCountDefault = 100;
        private const string Name = "name";
        private const bool RandomFileSizes = false;
        private const int SmallFileSize = 1024;
        private const int LargeFileSize = 5 * 1024 * 1024;
        private const HashType ContentHashType = HashType.Vso0;
        private static readonly Func<IReadOnlyContentSession, Task> EmptySetupFuncAsync = session => Task.FromResult(0);
        private static readonly CancellationToken Token = CancellationToken.None;
        private readonly Context _context;
        private readonly int _itemCount;
        private readonly AbsolutePath _prePopulatedRootPath;
        private readonly InitialSize _initialSize;
        private readonly PerformanceResultsFixture _resultsFixture;

        protected enum InitialSize
        {
            Empty, Full
        }

        private enum AccessNeeded
        {
            ReadOnly, Write
        }

        protected abstract IContentStore CreateStore(AbsolutePath rootPath, string cacheName, ContentStoreConfiguration configuration);

        protected abstract Task<IReadOnlyList<ContentHash>> EnumerateContentHashesAsync(IReadOnlyContentSession session);

        protected ContentPerformanceTests
            (
            ILogger logger,
            InitialSize initialSize,
            PerformanceResultsFixture resultsFixture,
            ITestOutputHelper output = null
            )
            : base(() => new PassThroughFileSystem(logger), logger, output)
        {
            _context = new Context(Logger);
            _initialSize = initialSize;
            _resultsFixture = resultsFixture;

            var itemCountEnvironmentVariable = Environment.GetEnvironmentVariable(ItemCountEnvironmentVariableName);
            _itemCount = itemCountEnvironmentVariable == null ? ItemCountDefault : int.Parse(itemCountEnvironmentVariable);
            _context.Debug($"Using itemCount=[{_itemCount}]");

            _prePopulatedRootPath = FileSystem.GetTempPath() / "CloudStore" / "ContentPerformanceTestsPrePopulated";
        }

        [Fact]
        public async Task PinNonExisting()
        {
            var hashes = Enumerable.Range(0, _itemCount).Select(x => ContentHash.Random()).ToList();
            var results = new List<PinResult>(_itemCount);

            await RunReadOnly(
                nameof(PinNonExisting),
                EmptySetupFuncAsync,
                session => PinAsync(session, hashes, results));

            results.All(r => r.Code == PinResult.ResultCode.ContentNotFound).Should().BeTrue();
        }

        [Fact]
        public async Task PinExisting()
        {
            IReadOnlyList<ContentHash> hashes = null;
            var results = new List<PinResult>(_itemCount);

            await RunReadOnly(
                nameof(PinExisting),
                async session => hashes = _initialSize == InitialSize.Full
                    ? await EnumerateContentHashesAsync(session) : await session.PutRandomAsync(
                        _context, ContentHashType, false, _itemCount, SmallFileSize, RandomFileSizes),
                async session => await PinAsync(session, hashes, results));

            foreach (var r in results)
            {
                r.Succeeded.Should().BeTrue(r.ToString());
            }
        }

        private async Task PinAsync(
            IReadOnlyContentSession session, IReadOnlyCollection<ContentHash> hashes, List<PinResult> results)
        {
            var tasks = hashes.Select(contentHash => Task.Run(async () =>
                await session.PinAsync(_context, contentHash, Token)));

            foreach (var task in tasks.ToList())
            {
                var result = await task;
                results.Add(result);
            }
        }

        [Fact]
        public async Task OpenStreamNonExisting()
        {
            var hashes = Enumerable.Range(0, _itemCount).Select(x => ContentHash.Random()).ToList();
            var results = new List<OpenStreamResult>(_itemCount);

            await RunReadOnly(
                nameof(OpenStreamNonExisting),
                EmptySetupFuncAsync,
                session => OpenStreamAsync(session, hashes, results));

            results.All(r => r.Code == OpenStreamResult.ResultCode.ContentNotFound).Should().BeTrue();
            results.All(r => r.Stream == null).Should().BeTrue();
        }

        [Fact]
        public async Task OpenStreamExisting()
        {
            IReadOnlyList<ContentHash> hashes = null;
            var results = new List<OpenStreamResult>(_itemCount);

            await RunReadOnly(
                nameof(OpenStreamExisting),
                async session => hashes = _initialSize == InitialSize.Full
                    ? await EnumerateContentHashesAsync(session) : await session.PutRandomAsync(
                        _context, ContentHashType, false, _itemCount, SmallFileSize, RandomFileSizes),
                async session => await OpenStreamAsync(session, hashes, results));

            results.All(r => r.Succeeded).Should().BeTrue();
            results.All(r => r.Stream != null).Should().BeTrue();
            results.ForEach(r =>
#pragma warning disable AsyncFixer02
                    r.Stream.Dispose());
#pragma warning restore AsyncFixer02
        }

        private async Task OpenStreamAsync(
            IReadOnlyContentSession session, IReadOnlyCollection<ContentHash> hashes, List<OpenStreamResult> results)
        {
            var tasks = hashes.Select(contentHash => Task.Run(async () =>
                await session.OpenStreamAsync(_context, contentHash, Token)));

            foreach (var task in tasks.ToList())
            {
                var result = await task;
                results.Add(result);
            }
        }

        [Fact]
        public async Task PlaceFileNonExisting()
        {
            using (var outputDirectory = new DisposableDirectory(FileSystem))
            {
                var hashes = Enumerable.Range(0, _itemCount).Select(x => ContentHash.Random()).ToList();
                IReadOnlyList<Tuple<ContentHash, AbsolutePath>> args =
                    hashes.Select(x => Tuple.Create(x, outputDirectory.Path / x.ToHex())).ToList();
                var results = new List<PlaceFileResult>(_itemCount);

                await RunReadOnly(
                    nameof(PlaceFileNonExisting),
                    EmptySetupFuncAsync,
                    async session => await PlaceFileAsync(session, args, results));

                results.All(r => r.Succeeded).Should().BeFalse();
            }
        }

        [Fact]
        public async Task PlaceFileExisting()
        {
            using (var outputDirectory = new DisposableDirectory(FileSystem))
            {
                IReadOnlyList<Tuple<ContentHash, AbsolutePath>> args = null;
                var results = new List<PlaceFileResult>(_itemCount);

                await RunReadOnly(
                    nameof(PlaceFileExisting),
                    async session =>
                    {
                        IReadOnlyList<ContentHash> hashes = _initialSize == InitialSize.Full
                            ? await EnumerateContentHashesAsync(session) : await session.PutRandomAsync(
                                _context, ContentHashType, false, _itemCount, SmallFileSize, RandomFileSizes);
                        args = hashes.Select(x => Tuple.Create(x, outputDirectory.Path / x.ToString())).ToList();
                    },
                    async session => await PlaceFileAsync(session, args, results));

                if (results.Any(r => !r.Succeeded))
                {
                    string message = "One or more error occurred: " + string.Join(Environment.NewLine, results.Select(s => s.ToString()));
                    Assert.True(false, message);
                }
            }
        }

        private async Task PlaceFileAsync(
            IReadOnlyContentSession session, IReadOnlyCollection<Tuple<ContentHash, AbsolutePath>> args, List<PlaceFileResult> results)
        {
            var tasks = args.Select(t => Task.Run(async () => await session.PlaceFileAsync(
                _context,
                t.Item1,
                t.Item2,
                FileAccessMode.ReadOnly,
                FileReplacementMode.FailIfExists,
                FileRealizationMode.HardLink,
                CancellationToken.None)));

            foreach (var task in tasks.ToList())
            {
                var result = await task;
                results.Add(result);
            }
        }

        [Fact]
        public async Task PutStreamNonExisting()
        {
            var streams = Enumerable.Range(0, _itemCount).Select(x =>
                new MemoryStream(ThreadSafeRandom.GetBytes(SmallFileSize))).ToList();
            var results = new List<PutResult>(_itemCount);

            await Run(
                nameof(PutStreamNonExisting),
                session => Task.FromResult(0),
                session => PutStreamAsync(session, streams, results));

            streams.ForEach(s =>
#pragma warning disable AsyncFixer02
                s.Dispose());
#pragma warning restore AsyncFixer02
            results.All(r => r.Succeeded).Should().BeTrue();
        }

        private async Task PutStreamAsync(
            IContentSession session, IReadOnlyCollection<Stream> streams, List<PutResult> results)
        {
            var tasks = streams.Select(s => Task.Run(async () =>
                await session.PutStreamAsync(_context, ContentHashType, s, CancellationToken.None)));

            foreach (var task in tasks.ToList())
            {
                var result = await task;
                results.Add(result);
            }
        }

        [Fact]
        public async Task PutFileNonExisting()
        {
            using (var inputDirectory = new DisposableDirectory(FileSystem))
            {
                var paths = new List<AbsolutePath>(_itemCount);
                var results = new List<PutResult>(_itemCount);

                await Run(
                    nameof(PutFileNonExisting),
                    session =>
                    {
                        foreach (var hash in Enumerable.Range(0, _itemCount).Select(x => ContentHash.Random()))
                        {
                            var path = inputDirectory.Path / hash.ToHex();
                            var content = ThreadSafeRandom.GetBytes(SmallFileSize);
                            FileSystem.WriteAllBytes(path, content);
                            paths.Add(path);
                        }
                        return Task.FromResult(0);
                    },
                    async session => await PutFileAsync(session, paths, results));
            }
        }

        private async Task PutFileAsync(
            IContentSession session, IReadOnlyList<AbsolutePath> paths, List<PutResult> results)
        {
            var tasks = paths.Select(p => Task.Run(async () => await session.PutFileAsync(
                _context, ContentHashType, p, FileRealizationMode.HardLink, CancellationToken.None)));

            foreach (var task in tasks.ToList())
            {
                var result = await task;
                results.Add(result);
            }
        }

        private async Task<AbsolutePath> EstablishTestContent(DisposableDirectory testDirectory, AccessNeeded accessNeeded)
        {
            AbsolutePath rootPath;

            if (accessNeeded == AccessNeeded.Write)
            {
                if (_initialSize == InitialSize.Full)
                {
                    _context.Debug("Starting with a full writable store");

                    await CreatePrepopulatedIfMissing();
                    await CopyPrepopulatedTo(testDirectory);

                    // Test runs against a writable copy in the test directory.
                    rootPath = testDirectory.Path;
                }
                else
                {
                    _context.Debug("Starting with an empty writable store");

                    // Test runs against an empty test directory.
                    rootPath = testDirectory.Path;
                }
            }
            else
            {
                if (_initialSize == InitialSize.Full)
                {
                    _context.Debug("Starting with a full read-only store");

                    await CreatePrepopulatedIfMissing();

                    // Test runs against the prepopulated directory.
                    rootPath = _prePopulatedRootPath;
                }
                else
                {
                    _context.Debug("Starting with an empty read-only store");

                    // Test runs against an empty test directory.
                    rootPath = testDirectory.Path;
                }
            }

            return rootPath;
        }

        private async Task CreatePrepopulatedIfMissing()
        {
            if (FileSystem.DirectoryExists(_prePopulatedRootPath))
            {
                return;
            }

            _context.Always($"Create prepopulated content store at root=[{_prePopulatedRootPath}]");
            FileSystem.CreateDirectory(_prePopulatedRootPath);
            await RunStore(
                _prePopulatedRootPath,
                CacheName,
                session => session.PutRandomAsync(_context, ContentHashType, false, MaxSize, 100, LargeFileSize, RandomFileSizes));
        }

        private async Task CopyPrepopulatedTo(DisposableDirectory testDirectory)
        {
            foreach (FileInfo fileInfo in FileSystem.EnumerateFiles(_prePopulatedRootPath, EnumerateOptions.Recurse))
            {
                var sourcePath = fileInfo.FullPath;
                var destinationPath = sourcePath.SwapRoot(_prePopulatedRootPath, testDirectory.Path);
                await FileSystem.CopyFileAsync(sourcePath, destinationPath, false);
            }
        }

        private Task RunReadOnly
            (
            string method,
            Func<IContentSession, Task> setupFuncAsync,
            Func<IReadOnlyContentSession, Task> testFuncAsync
            )
        {
            return RunImpl(method, AccessNeeded.ReadOnly, setupFuncAsync, async session =>
                await testFuncAsync(session));
        }

        private Task Run
            (
            string method,
            Func<IContentSession, Task> setupFuncAsync,
            Func<IContentSession, Task> testFuncAsync
            )
        {
            return RunImpl(method, AccessNeeded.Write, setupFuncAsync, testFuncAsync);
        }

        private async Task RunImpl
            (
            string method,
            AccessNeeded accessNeeded,
            Func<IContentSession, Task> setupFuncAsync,
            Func<IContentSession, Task> testFuncAsync
            )
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath rootPath = await EstablishTestContent(testDirectory, accessNeeded);

                await RunStore(rootPath, CacheName, async session =>
                {
                    if (setupFuncAsync != null)
                    {
                        await setupFuncAsync(session);
                    }

                    var stopwatch = Stopwatch.StartNew();
                    await testFuncAsync(session);
                    stopwatch.Stop();

                    var rate = (long)(_itemCount / stopwatch.Elapsed.TotalSeconds);
                    var name = GetType().Name + "." + method;
                    _resultsFixture.AddResults(Output, name, rate, "items/sec", _itemCount);
                });
            }
        }

        private async Task RunStore(AbsolutePath rootPath, string cacheName, Func<IContentSession, Task> funcAsync)
        {
            var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB((uint)MaxSizeInMegabytes);
            using (var store = CreateStore(rootPath, cacheName, configuration))
            {
                try
                {
                    await store.StartupAsync(_context).ShouldBeSuccess();

                    var createSessionResult = store.CreateSession(_context, Name, ImplicitPin.PutAndGet).ShouldBeSuccess();

                    using (var session = createSessionResult.Session)
                    {
                        try
                        {
                            await session.StartupAsync(_context).ShouldBeSuccess();

                            await funcAsync(session);
                        }
                        finally
                        {
                            await session.ShutdownAsync(_context).ShouldBeSuccess();
                        }
                    }
                }
                finally
                {
                    await store.ShutdownAsync(_context).ShouldBeSuccess();
                }
            }
        }
    }
}
