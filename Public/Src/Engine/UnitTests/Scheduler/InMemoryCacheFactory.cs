// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Utilities.Core;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Factory for in-memory cache layers.
    /// </summary>
    public static class InMemoryCacheFactory
    {
        public static EngineCache Create()
        {
            return new EngineCache(
                new InMemoryArtifactContentCache(),
                new InMemoryTwoPhaseFingerprintStore());
        }
    }
}
