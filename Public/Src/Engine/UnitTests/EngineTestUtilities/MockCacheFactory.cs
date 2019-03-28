// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;

namespace Test.BuildXL.EngineTestUtilities
{
    /// <summary>
    /// Class for creating mock cache.
    /// </summary>
    public static class MockCacheFactory
    {
        /// <summary>
        /// Creates an instance of mock cache consisting of <see cref="MockArtifactContentCache"/> and <see cref="InMemoryTwoPhaseFingerprintStore"/>.
        /// </summary>
        public static EngineCache Create(string root)
        {
            return new EngineCache(new MockArtifactContentCache(root), new InMemoryTwoPhaseFingerprintStore());
        }
    }
}
