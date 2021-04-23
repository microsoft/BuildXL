// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if MICROSOFT_INTERNAL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts;
using BuildXL.Cache.MemoizationStore.Vsts.Internal;
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

        protected override ICachePublisher CreatePublisher(
            BuildCacheServiceConfiguration config,
            string pat,
            Context context) => new DummyPublisher(config.ForceUpdateOnAddContentHashList);

        private class DummyPublisher : StartupShutdownSlimBase, ICachePublisher
        {
            private readonly HashSet<ContentHash> _storedHashes = new HashSet<ContentHash>();
            private readonly Dictionary<StrongFingerprint, ContentHashListWithDeterminism> _storedHashLists = new Dictionary<StrongFingerprint, ContentHashListWithDeterminism>();

            private readonly bool _forceUpdate;

            public DummyPublisher(bool forceUpdate)
            {
                _forceUpdate = forceUpdate;
            }

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