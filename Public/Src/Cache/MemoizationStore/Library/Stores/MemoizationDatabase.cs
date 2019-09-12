// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    /// Defines a database which stores memoization information
    /// </summary>
    public abstract class MemoizationDatabase : StartupShutdownSlimBase, IName
    {
        /// <summary>
        /// Gets the name of the component
        /// </summary>
        public string Name => Tracer.Name;

        /// <summary>
        /// Performs a compare exchange operation on metadata, while ensuring all invariants are kept. If the
        /// fingerprint is not present, then it is inserted.
        /// </summary>
        public abstract Task<Result<bool>> CompareExchange(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            string expectedReplacementToken,
            ContentHashListWithDeterminism expected,
            ContentHashListWithDeterminism replacement);

        /// <summary>
        /// Load a ContentHashList and the token used to replace it.
        /// </summary>
        public abstract Task<Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint);

        /// <summary>
        /// Enumerates all strong fingerprints
        /// </summary>
        public abstract Task<IEnumerable<StructResult<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context);

        /// <summary>
        /// <see cref="ILevelSelectorsProvider.GetLevelSelectorsAsync(Context, Fingerprint, CancellationToken, int)"/>
        /// </summary>
        public abstract Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level);
    }
}
