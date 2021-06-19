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
    public abstract class MemoizationDatabase : StartupShutdownComponentBase, IName
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
                extraStartMessage: $"StrongFingerprint=[{strongFingerprint}] ExpectedReplacementToken=[{expectedReplacementToken}] Expected=[{expected.ToTraceString()}] Replacement=[{replacement.ToTraceString()}]",
                extraEndMessage: result => $"StrongFingerprint=[{strongFingerprint}] ExpectedReplacementToken=[{expectedReplacementToken}] Expected=[{expected.ToTraceString()}] Replacement=[{replacement.ToTraceString()}] Exchanged=[{result.GetValueOrDefault(false)}]",
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
        public Task<ContentHashListResult> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                nestedContext => GetContentHashListCoreAsync(nestedContext, strongFingerprint, preferShared),
                extraStartMessage: $"StrongFingerprint=[{strongFingerprint}], PreferShared=[{preferShared}]",
                extraEndMessage: result => getStringResult(result),
                timeout: _timeout);

            string getStringResult(ContentHashListResult result)
            {
                var resultString = $"StrongFingerprint=[{strongFingerprint}], PreferShared=[{preferShared}] Result=[{result.GetValueOrDefault().contentHashListInfo.ToTraceString()}] Token=[{result.GetValueOrDefault().replacementToken}]";

                if (result.Source != ContentHashListSource.Unknown)
                {
                    resultString += $", Source=[{result.Source}]";
                }
                
                return resultString;
            }
        }

        /// <nodoc />
        protected abstract Task<ContentHashListResult> GetContentHashListCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared);

        /// <summary>
        /// Enumerates all strong fingerprints
        /// </summary>
        public abstract Task<IEnumerable<Result<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context);

        /// <summary>
        /// <see cref="ILevelSelectorsProvider.GetLevelSelectorsAsync(Context, Fingerprint, CancellationToken, int)"/>
        /// </summary>
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                nestedContext => GetLevelSelectorsCoreAsync(nestedContext, weakFingerprint, level),
                extraEndMessage: result =>
                {
                    var numSelectors = 0;
                    bool hasMore = false;
                    if (result.Succeeded)
                    {
                        numSelectors = result.Value.Selectors.Count;
                        hasMore = result.Value.HasMore;
                    }

                    return $"WeakFingerprint=[{weakFingerprint}] Level=[{level}] NumSelectors=[{numSelectors}] HasMore=[{hasMore}]";
                },
                timeout: _timeout);
        }

        /// <nodoc />
        protected abstract Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level);
    }
}
