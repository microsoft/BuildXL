// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
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
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _timeout;

        /// <summary>
        /// Gets the name of the component
        /// </summary>
        public string Name => Tracer.Name;

        /// <nodoc />
        protected MemoizationDatabase(TimeSpan? operationsTimeout = null)
        {
            _timeout = operationsTimeout ?? DefaultTimeout;
        }

        /// <summary>
        /// Performs a compare exchange operation on metadata, while ensuring all invariants are kept. If the
        /// fingerprint is not present, then it is inserted.
        /// </summary>
        public Task<Result<bool>> CompareExchange(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            string expectedReplacementToken,
            ContentHashListWithDeterminism expected,
            ContentHashListWithDeterminism replacement)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                nestedContext => CompareExchangeCore(nestedContext, strongFingerprint, expectedReplacementToken, expected, replacement),
                extraStartMessage: $"StrongFingerprint=({strongFingerprint}) expected=[{expected.ToTraceString()}] replacement=[{replacement.ToTraceString()}]",
                extraEndMessage: _ => $"StrongFingerprint=({strongFingerprint})  expected=[{expected.ToTraceString()}] replacement=[{replacement.ToTraceString()}]",
                timeout: _timeout);
        }

        /// <nodoc />
        protected abstract Task<Result<bool>> CompareExchangeCore(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            string expectedReplacementToken,
            ContentHashListWithDeterminism expected,
            ContentHashListWithDeterminism replacement);

        /// <summary>
        /// Load a ContentHashList and the token used to replace it.
        /// </summary>
        public Task<Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                nestedContext => GetContentHashListCoreAsync(nestedContext, strongFingerprint, preferShared),
                extraStartMessage: $"StrongFingerprint=[{strongFingerprint}], PreferShared=[{preferShared}]",
                extraEndMessage: result => $"StrongFingerprint=[{strongFingerprint}], PreferShared=[{preferShared}] Result=[{result.GetValueOrDefault().contentHashListInfo.ToTraceString()}] Token=[{result.GetValueOrDefault().replacementToken}]",
                timeout: _timeout);
        }

        /// <nodoc />
        protected abstract Task<Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>> GetContentHashListCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared);

        /// <summary>
        /// Enumerates all strong fingerprints
        /// </summary>
        public abstract Task<IEnumerable<StructResult<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context);

        /// <summary>
        /// <see cref="ILevelSelectorsProvider.GetLevelSelectorsAsync(Context, Fingerprint, CancellationToken, int)"/>
        /// </summary>
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                nestedContext => GetLevelSelectorsCoreAsync(nestedContext, weakFingerprint, level),
                extraEndMessage: _ => $"WeakFingerprint=[{weakFingerprint}], Level=[{level}]",
                traceErrorsOnly: true,
                timeout: _timeout);
        }

        /// <nodoc />
        protected abstract Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level);
    }
}
