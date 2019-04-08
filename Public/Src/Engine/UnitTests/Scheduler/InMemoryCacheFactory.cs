// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Utilities;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Factory for in-memory cache layers.
    /// </summary>
    public static class InMemoryCacheFactory
    {
        public static EngineCache Create(PipExecutionContext context)
        {
            return new EngineCache(
                new InMemoryArtifactContentCache(context),
                new InMemoryTwoPhaseFingerprintStore());
        }
    }
}
