// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;

namespace BuildXL.Engine.Cache
{
    /// <summary>
    /// The facets of BuildXL's caching layer.
    /// Note that a <see cref="EngineCache"/> owns rather than borrows its facets, and reserves the right to dispose them.
    /// </summary>
    public sealed class EngineCache : IDisposable
    {
        /// <summary>
        /// Storage of artifact content - a content-addressable store.
        /// </summary>
        public readonly IArtifactContentCache ArtifactContentCache;

        /// <summary>
        /// New-style two-phase lookup of pip execution info, using both weak and strong content fingerprints.
        /// </summary>
        public readonly ITwoPhaseFingerprintStore TwoPhaseFingerprintStore;

        /// <summary>
        /// Creates a <see cref="EngineCache"/> which owns the provided facets.
        /// The facets will be disposed when the new instance is disposed.
        /// </summary>
        public EngineCache(
            IArtifactContentCache contentCache,
            ITwoPhaseFingerprintStore twoPhaseFingerprintStore)
        {
            Contract.Requires(contentCache != null);
            Contract.Requires(twoPhaseFingerprintStore != null);

            ArtifactContentCache = contentCache;
            TwoPhaseFingerprintStore = twoPhaseFingerprintStore;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DisposeIfSupported(ArtifactContentCache);
            DisposeIfSupported(TwoPhaseFingerprintStore);
        }

        private static void DisposeIfSupported(object o)
        {
            IDisposable d = o as IDisposable;
            if (d != null)
            {
                d.Dispose();
            }
        }
    }
}
