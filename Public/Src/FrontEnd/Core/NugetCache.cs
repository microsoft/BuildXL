// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.SinglePhase;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Helper class that represents a cache of nuget packages.
    /// </summary>
    internal sealed class NugetCache : ICriticalNotifyCompletion
    {
        private readonly Task<Possible<EngineCache>> m_engineCache;
        private readonly LoggingContext m_loggingContext;
        private readonly Lazy<SinglePhaseFingerprintStoreAdapter> m_lazySinglePhaseAdapter;
        private readonly ConcurrentDictionary<string, ContentFingerprint> m_fingerprintCache = new ConcurrentDictionary<string, ContentFingerprint>();

        public NugetCache(Task<Possible<EngineCache>> engineCache, PathTable pathTable, LoggingContext loggingContext)
        {
            Contract.Requires(engineCache != null);
            Contract.Requires(pathTable != null);

            m_engineCache = engineCache;
            m_loggingContext = loggingContext;
            m_lazySinglePhaseAdapter = new Lazy<SinglePhaseFingerprintStoreAdapter>(
                () =>
                {
                    return new SinglePhaseFingerprintStoreAdapter(
                        m_loggingContext,
                        pathTable,
                        Cache.TwoPhaseFingerprintStore,
                        Cache.ArtifactContentCache);
                });
        }

        /// <summary>
        /// Returns true when the cache initialized successfully
        /// </summary>
        public bool Succeeded => m_engineCache.Result.Succeeded;

        /// <summary>
        /// Returns a cache initialization failure. Valid only when <see cref="Succeeded"/> returns false.
        /// </summary>
        public Failure Failure => m_engineCache.Result.Failure;

        /// <summary>
        /// Returns 'this'.
        /// </summary>
        /// <remarks>
        /// Part of the awaitable API.
        /// The intended usage is:
        /// <code>NugetCache cache = await m_nugetCache;</code>
        /// </remarks>
        public NugetCache GetResult() => this;

        /// <summary>
        /// Returns true when the cache is ready.
        /// </summary>
        /// <remarks>
        /// Part of the awaitable API.
        /// </remarks>
        public bool IsCompleted => m_engineCache.IsCompleted;

        /// <summary>
        /// Gets the initialized cache.
        /// Valid when the initialization succeeded.
        /// </summary>
        public EngineCache Cache
        {
            get
            {
                Contract.Requires(Succeeded);
                return m_engineCache.GetAwaiter().GetResult().Result;
            }
        }

        /// <summary>
        /// Gets the single phase store adapter.
        /// Valid when the initialization succeeded.
        /// </summary>
        public SinglePhaseFingerprintStoreAdapter SinglePhaseStore
        {
            get
            {
                Contract.Requires(Succeeded);
                return m_lazySinglePhaseAdapter.Value;
            }
        }

        /// <summary>
        /// Gets the fingerprint for a given weak package fingerprint.
        /// </summary>
        public ContentFingerprint GetDownloadFingerprint(string weakPackageFingerprint)
        {
            return m_fingerprintCache.GetOrAdd(
                weakPackageFingerprint,
                key => CreateDownloadFingerprint("package:\n" + key));
        }

        /// <inheritdoc />
        public void OnCompleted(Action continuation)
        {
            m_engineCache.GetAwaiter().OnCompleted(continuation);
        }

        /// <inheritdoc />
        public void UnsafeOnCompleted(Action continuation)
        {
            m_engineCache.GetAwaiter().UnsafeOnCompleted(continuation);
        }

        /// <summary>
        /// Returns the current instance.
        /// </summary>
        /// <remarks>
        /// Part of the awaitable API.
        /// </remarks>
        public NugetCache GetAwaiter() => this;

        private static ContentFingerprint CreateDownloadFingerprint(string baseText)
        {
            // In case something in the cached Bond data becomes incompatible, we must not match.
            const string VersionText = ", BondDataVersion=2;FingerprintVersion=1";
            var fingerprint = FingerprintUtilities.Hash(baseText + VersionText);
            return new ContentFingerprint(fingerprint);
        }
    }
}
