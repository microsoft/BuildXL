// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Utilities;

namespace Test.BuildXL.EngineTestUtilities
{
    /// <summary>
    /// In memory cache for testing. Allows reuse of data between engine invocations by
    /// reusing this object to initialize cache.
    /// </summary>
    public sealed class TestCache
    {
        private InMemoryArtifactContentCache m_artifacts;

        /// <summary>
        /// The in memory fingerprints store for testing
        /// </summary>
        public readonly InMemoryTwoPhaseFingerprintStore Fingerprints;

        /// <nodoc />
        public TestCache()
        {
            Fingerprints = new InMemoryTwoPhaseFingerprintStore();
        }

        /// <summary>
        /// Gets an artifact cache for the given context
        /// </summary>
        public InMemoryArtifactContentCache GetArtifacts()
        {
            if (m_artifacts == null)
            {
                m_artifacts = new InMemoryArtifactContentCache();
            }
            else
            {
                m_artifacts = m_artifacts.Wrap();
            }

            return m_artifacts;
        }
    }
}
