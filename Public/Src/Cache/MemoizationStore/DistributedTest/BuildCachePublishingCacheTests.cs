// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if MICROSOFT_INTERNAL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class BuildCachePublishingCacheTests : PublishingCacheTests
    {
        protected override PublishingCacheConfiguration CreateConfiguration(bool publishAsynchronously)
            => new BuildCacheServiceConfiguration(
                cacheServiceContentEndpoint: "contentEndpoint",
                cacheServiceFingerprintEndpoint: "fingerprintEndpoint")
            {
                PublishAsynchronously = publishAsynchronously
            };

        protected override IPublishingStore CreatePublishingStore(IContentStore contentStore)
            => new BuildCacheTestPublishingStore(contentStore, FileSystem, concurrencyLimit: 1);
    }

    public class BuildCacheTestPublishingStore : BuildCachePublishingStore
    {
        public BuildCacheTestPublishingStore(IContentStore contentSource, IAbsFileSystem fileSystem, int concurrencyLimit) : base(contentSource, fileSystem, concurrencyLimit)
        {
        }

        public override Result<IPublishingSession> CreateSession(Context context, string name, PublishingCacheConfiguration config, string pat)
        {
            if (config is not BuildCacheServiceConfiguration buildCacheConfig)
            {
                return new Result<IPublishingSession>($"Configuration is not a {nameof(BuildCacheServiceConfiguration)}. Actual type: {config.GetType().FullName}");
            }

            var contentSessionResult = ContentSource.CreateSession(context, $"{name}-contentSource", ImplicitPin.None);
            if (!contentSessionResult.Succeeded)
            {
                return new Result<IPublishingSession>(contentSessionResult);
            }

            var configuration = new BuildCachePublishingSessionConfiguration()
            {
                BuildCacheConfiguration = buildCacheConfig,
                PersonalAccessToken = pat,
                SessionName = name,
            };
            return new Result<IPublishingSession>(new BuildCacheTestPublishingSession(configuration, contentSessionResult.Session, FingerprintPublishingGate, ContentPublishingGate));
        }

        private class BuildCacheTestPublishingSession : BuildCachePublishingSession
        {
            public BuildCacheTestPublishingSession(
                BuildCachePublishingSessionConfiguration configuration,
                IContentSession contentSource,
                SemaphoreSlim fingerprintPublishingGate,
                SemaphoreSlim contentPublishingGate)
                : base(configuration, contentSource, fingerprintPublishingGate, contentPublishingGate)
            {
            }

            protected override Task<ICachePublisher> CreateCachePublisherCoreAsync(
                OperationContext context, BuildCachePublishingSessionConfiguration configuration)
            {
                return Task.FromResult<ICachePublisher>(new DummyPublisher(configuration.BuildCacheConfiguration.ForceUpdateOnAddContentHashList));
            }
        }

        private class DummyPublisher : StartupShutdownSlimBase, ICachePublisher
        {
            private readonly HashSet<ContentHash> _storedHashes = new HashSet<ContentHash>();
            private readonly Dictionary<StrongFingerprint, ContentHashListWithDeterminism> _storedHashLists = new Dictionary<StrongFingerprint, ContentHashListWithDeterminism>();

            private readonly bool _forceUpdate;

            public DummyPublisher(bool forceUpdate)
            {
                _forceUpdate = forceUpdate;
            }

            public Guid CacheGuid => Guid.Empty;

            protected override Tracer Tracer { get; } = new Tracer(nameof(DummyPublisher));

            public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
                Context context,
                StrongFingerprint strongFingerprint,
                ContentHashListWithDeterminism contentHashListWithDeterminism,
                CancellationToken cts,
                UrgencyHint urgencyHint = UrgencyHint.Nominal)
            {
                foreach (var hash in contentHashListWithDeterminism.ContentHashList.Hashes)
                {
                    Assert.True(_storedHashes.Contains(hash));
                }

                if (_forceUpdate)
                {
                    _storedHashLists.Remove(strongFingerprint);
                }

                var added = !_storedHashLists.ContainsKey(strongFingerprint);

                if (added)
                {
                    _storedHashLists[strongFingerprint] = contentHashListWithDeterminism;
                    return Task.FromResult(new AddOrGetContentHashListResult(default(ContentHashListWithDeterminism)));
                }

                return Task.FromResult(new AddOrGetContentHashListResult(_storedHashLists[strongFingerprint]));
            }

            public Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            {
                if (_storedHashLists.ContainsKey(strongFingerprint))
                {
                    return Task.FromResult(new GetContentHashListResult(_storedHashLists[strongFingerprint]));
                };

                return Task.FromResult(new GetContentHashListResult(default(ContentHashListWithDeterminism)));
            }

            public Task<BoolResult> IncorporateStrongFingerprintsAsync(OperationContext context, IEnumerable<Task<StrongFingerprint>> strongFingerprints)
            {
                return BoolResult.SuccessTask;
            }

            public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
                => Task.FromResult(contentHashes
                    .Select((hash, index)
                        => Task.FromResult(
                            new Indexed<PinResult>(
                                new PinResult(_storedHashes.Contains(hash) ? PinResult.ResultCode.Success : PinResult.ResultCode.ContentNotFound), index))));

            public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            {
                _storedHashes.Add(contentHash);
                return Task.FromResult(new PutResult(contentHash, stream.Length));
            }
        }
    }
}
#endif
