// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Engine;
using Xunit;

namespace Test.BuildXL.Engine
{
    public class BuildSentinelTests : IDisposable
    {
        private readonly string m_tempDir;
        private readonly string m_engineCacheDir;
        private readonly string m_sentinelPath;

        public BuildSentinelTests()
        {
            m_tempDir = Path.Combine(Path.GetTempPath(), "BuildXL.BuildSentinelTests." + Guid.NewGuid().ToString("N"));
            m_engineCacheDir = Path.Combine(m_tempDir, "EngineCache");
            m_sentinelPath = Path.Combine(m_tempDir, "sentinel", ".hasrun");
            Directory.CreateDirectory(m_engineCacheDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(m_tempDir))
            {
                Directory.Delete(m_tempDir, recursive: true);
            }
        }

        [Fact]
        public void DetectionProgression()
        {
            var sentinel = new BuildSentinel(m_engineCacheDir, m_sentinelPath);

            // 1. Empty EngineCache + no sentinel file = clean machine
            Assert.False(sentinel.HasPriorBuild());

            // 2. Empty subdirectories (like CreateOutputDirectories creates) don't count
            Directory.CreateDirectory(Path.Combine(m_engineCacheDir, "FingerprintStore"));
            Directory.CreateDirectory(Path.Combine(m_engineCacheDir, "SharedOpaqueSidebandFiles"));
            Assert.False(sentinel.HasPriorBuild());

            // 3. A top-level file in EngineCache from a prior bxl version is detected (fallback)
            File.Create(Path.Combine(m_engineCacheDir, "FileContentTable")).Dispose();
            Assert.True(sentinel.HasPriorBuild());

            // 4. TryMarkBuildStarted writes the sentinel file; detection works via sentinel alone
            File.Delete(Path.Combine(m_engineCacheDir, "FileContentTable"));
            Assert.True(sentinel.TryMarkBuildStarted());
            Assert.True(sentinel.HasPriorBuild());

            // 5. Calling TryMarkBuildStarted again is safe (idempotent)
            Assert.True(sentinel.TryMarkBuildStarted());
        }

        [Fact]
        public void NonexistentEngineCacheDirectoryIsClean()
        {
            var sentinel = new BuildSentinel(Path.Combine(m_tempDir, "doesNotExist"), m_sentinelPath);
            Assert.False(sentinel.HasPriorBuild());
        }
    }
}
