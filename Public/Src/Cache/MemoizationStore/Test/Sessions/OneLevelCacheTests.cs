// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class OneLevelCacheTests : CacheTests
    {
        private const UrgencyHint NonDefaultUrgencyHint = UrgencyHint.Nominal + 1;
        private const FileAccessMode AccessMode = FileAccessMode.ReadOnly;
        private const FileReplacementMode ReplacementMode = FileReplacementMode.ReplaceExisting;
        private const FileRealizationMode RealizationMode = FileRealizationMode.Copy;
        private const HashType HasherType = HashType.Vso0;

        private static readonly AbsolutePath Path = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("Z", "Test", "test.txt"));
        private readonly BackgroundTaskTracker _taskTracker;
        private readonly Context _backgroundContext;

        private readonly TestContentSession _mockContentSession;
        private readonly TestContentStore _mockContentStore;
        private readonly TestMemoizationSession _mockMemoizationSession;
        private readonly TestMemoizationStore _mockMemoizationStore;

        public OneLevelCacheTests()
            : base(() => new MemoryFileSystem(TestSystemClock.Instance), TestGlobal.Logger)
        {
            _taskTracker = new BackgroundTaskTracker(nameof(OneLevelCacheTests), new Context(Logger));
            _backgroundContext = new Context(Logger);
            
            _mockContentSession = new TestContentSession();
            _mockContentStore = new TestContentStore(_mockContentSession);
            _mockMemoizationSession = new TestMemoizationSession();
            _mockMemoizationStore = new TestMemoizationStore(_mockMemoizationSession);
        }

        protected override void Dispose(bool disposing)
        {
            _taskTracker.Synchronize().GetAwaiter().GetResult();
            _taskTracker.ShutdownAsync(_backgroundContext).Wait();
            _taskTracker.Dispose();
            base.Dispose(disposing);
        }

        protected override ICache CreateCache(DisposableDirectory testDirectory)
        {
            var rootPath = testDirectory.Path;
            var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
            var configurationModel = new ConfigurationModel(configuration);

            return new OneLevelCache(
                () => new FileSystemContentStore(FileSystem, SystemClock.Instance, rootPath, configurationModel),
                () => new MemoryMemoizationStore(Logger),
                CacheDeterminism.NewCacheGuid());
        }

        [Fact]
        public Task PinPassThrough()
        {
            var context = new Context(Logger);
            var contentHash = ContentHash.Random();

            return RunMockSessionTestAsync(context, session =>
            {
                session.PinAsync(context, contentHash, Token, NonDefaultUrgencyHint).ConfigureAwait(false);
                _mockContentSession.Pinned.Contains(contentHash);
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task OpenStreamPassThrough()
        {
            var context = new Context(Logger);
            var contentHash = ContentHash.Random();

            return RunMockSessionTestAsync(context, session =>
            {
                session.OpenStreamAsync(context, contentHash, Token, NonDefaultUrgencyHint).ConfigureAwait(false).GetAwaiter().GetResult().ShouldBeSuccess();
                _mockContentSession.OpenStreamed.Contains(contentHash);
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task PlaceFilePassThrough()
        {
            var context = new Context(Logger);
            var contentHash = ContentHash.Random();
            var path = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("Z", "test.txt"));

            return RunMockSessionTestAsync(context, session =>
            {
                session.PlaceFileAsync(
                    context,
                    contentHash,
                    path,
                    AccessMode,
                    ReplacementMode,
                    RealizationMode,
                    Token,
                    NonDefaultUrgencyHint).ConfigureAwait(false).GetAwaiter().GetResult().IgnoreFailure();
                _mockContentSession.FilePlacedParams.Contains(new Tuple<ContentHash, AbsolutePath, FileAccessMode, FileReplacementMode, FileRealizationMode>(contentHash, path, AccessMode, ReplacementMode, RealizationMode));
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task PutFileHashTypePassThrough()
        {
            var context = new Context(Logger);

            return RunMockSessionTestAsync(context, session =>
            {
                session.PutFileAsync(context, HasherType, Path, RealizationMode, Token, NonDefaultUrgencyHint).ConfigureAwait(false).GetAwaiter().GetResult().ShouldBeSuccess();
                Assert.True(_mockContentSession.PutFileHashTypeParams.Contains(new Tuple<HashType, AbsolutePath, FileRealizationMode>(HasherType, Path, RealizationMode)), $"Expected to find ({HasherType},{Path},{RealizationMode}) in set of put files.");
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task PutFileHashPassThrough()
        {
            var context = new Context(Logger);
            var contentHash = ContentHash.Random();

            return RunMockSessionTestAsync(context, session =>
            {
                session.PutFileAsync(context, contentHash, Path, RealizationMode, Token, NonDefaultUrgencyHint).ConfigureAwait(false).GetAwaiter().GetResult().ShouldBeSuccess();
                Assert.True(_mockContentSession.PutFileHashParams.Contains(new Tuple<ContentHash, AbsolutePath, FileRealizationMode>(contentHash, Path, RealizationMode)), $"Expected to find ({contentHash},{Path},{RealizationMode}) in set of put files.");
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task PutStreamHashTypePassThrough()
        {
            var context = new Context(Logger);
            var stream = new MemoryStream();

            return RunMockSessionTestAsync(context, session =>
            {
                session.PutStreamAsync(context, HasherType, stream, Token, NonDefaultUrgencyHint).ConfigureAwait(false).GetAwaiter().GetResult().ShouldBeSuccess();
                Assert.True(_mockContentSession.PutStreamHashTypeParams.Contains(HasherType), $"Expected to find ({HasherType}) in set of put streams.");
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task PutStreamHashPassThrough()
        {
            var context = new Context(Logger);
            var contentHash = ContentHash.Random();
            var stream = new MemoryStream();

            return RunMockSessionTestAsync(context, session =>
            {
                session.PutStreamAsync(context, contentHash, stream, Token, NonDefaultUrgencyHint).ConfigureAwait(false).GetAwaiter().GetResult().ShouldBeSuccess();
                Assert.True(_mockContentSession.PutStreamHashParams.Contains(contentHash), $"Expected to find ({contentHash}) in set of put streams.");
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task GetSelectorsPassThrough()
        {
            var context = new Context(Logger);
            var weakFingerprint = Fingerprint.Random();

            return RunMockSessionTestAsync(context, async session =>
            {
                await session.GetSelectors(context, weakFingerprint, Token, NonDefaultUrgencyHint).ToArray();
                Assert.True(_mockMemoizationSession.GetSelectorsParams.Contains(weakFingerprint), $"Expected to find ({weakFingerprint}) in set of GetSelectors calls.");
            });
        }

        [Fact]
        public Task GetContentHashListPassThrough()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();

            return RunMockReadOnlySessionTestAsync(context, session =>
            {
                session.GetContentHashListAsync(context, strongFingerprint, Token, NonDefaultUrgencyHint).ConfigureAwait(false).GetAwaiter().GetResult().ShouldBeSuccess();
                Assert.True(_mockMemoizationSession.GetContentHashListAsyncParams.Contains(strongFingerprint));
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task AddOrGetContentHashListPassThrough()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(),
                CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires));

            return RunMockSessionTestAsync(context, session =>
            {
                session.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashListWithDeterminism, Token, NonDefaultUrgencyHint).ConfigureAwait(false).GetAwaiter().GetResult().ShouldBeSuccess();
                Assert.True(_mockMemoizationSession.AddOrGetContentHashListAsyncParams.Contains(new Tuple<StrongFingerprint, ContentHashListWithDeterminism>(strongFingerprint, contentHashListWithDeterminism)));
                return Task.FromResult(0);
            });
        }

        [Fact]
        public Task IncorporateStrongFingerprintsPassThrough()
        {
            var context = new Context(Logger);
            var strongFingerprints = new[] {Task.FromResult(StrongFingerprint.Random())};

            return RunMockSessionTestAsync(context, session =>
            {
                session.IncorporateStrongFingerprintsAsync(context, strongFingerprints, Token, NonDefaultUrgencyHint).ConfigureAwait(false).GetAwaiter().GetResult().ShouldBeSuccess();
                Assert.True(_mockMemoizationSession.IncorporateStringFingerprintsAsyncParams.Contains(strongFingerprints));
                return Task.FromResult(0);
            });
        }

        private Task RunMockSessionTestAsync(Context context, Func<ICacheSession, Task> funcAsync)
        {
            return RunTestAsync(
                context,
                funcAsync,
                testDirectory => new OneLevelCache(() => _mockContentStore, () => _mockMemoizationStore, CacheDeterminism.NewCacheGuid()));
        }

        private Task RunMockReadOnlySessionTestAsync(Context context, Func<IReadOnlyCacheSession, Task> funcAsync)
        {
            return RunReadOnlySessionTestAsync(
                context,
                funcAsync,
                testDirectory => new OneLevelCache(() => _mockContentStore, () => _mockMemoizationStore, CacheDeterminism.NewCacheGuid()));
        }
    }
}
