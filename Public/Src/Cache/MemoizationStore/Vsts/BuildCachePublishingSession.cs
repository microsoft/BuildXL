// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    internal class BuildCachePublishingSession : IPublishingSession
    {
        private readonly BuildCacheServiceConfiguration _config;
        private readonly BuildCachePublishingStore _store;
        private readonly string _pat;

        public BuildCachePublishingSession(BuildCacheServiceConfiguration config, string pat, BuildCachePublishingStore store)
        {
            _config = config;
            _store = store;
            _pat = pat;
        }

        public Task<BoolResult> PublishContentHashListAsync(
            Context context,
            StrongFingerprint fingerprint,
            ContentHashListWithDeterminism contentHashList,
            CancellationToken token)
            => _store.PublishContentHashListAsync(context, fingerprint, contentHashList, _config, _pat, token);
    }
}
