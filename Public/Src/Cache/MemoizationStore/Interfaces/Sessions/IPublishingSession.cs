// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <nodoc />
    public interface IPublishingSession
    {
        /// <nodoc />
        Task<BoolResult> PublishContentHashListAsync(
            Context context,
            StrongFingerprint fingerprint,
            ContentHashListWithDeterminism contentHashList,
            CancellationToken token);
    }
}
