// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace BuildXL.Engine
{
    /// <summary>
    /// Detects whether BuildXL has previously executed on this machine by checking for build
    /// artifacts. Used by the SkipScrubbingOnCleanMachine option to skip scrubbing on clean machines.
    ///
    /// Primary signal: a sentinel file in LocalApplicationData, written at the start of every
    /// build by TryMarkBuildStarted. This persists even if the EngineCache is deleted between builds.
    /// Fallback signal: any top-level file in the EngineCache directory (e.g., FileContentTable,
    /// StringTable left by prior bxl versions). Uses TopDirectoryOnly to avoid traversing junctions.
    /// This fallback signal was primarily added for the BuildXL repo validation patter where it uses the
    /// old version of the build engine and new version back to back. This fallback check can be removed after
    /// BuildXL is released with this new functionality.
    /// </summary>
    internal class BuildSentinel
    {
        private const string SentinelDirectoryName = "BuildXL";
        private const string SentinelFileName = ".hasrun";

        private readonly string m_engineCacheDirectory;
        private readonly string m_sentinelFilePath;

        /// <summary>
        /// Creates a BuildSentinel.
        /// </summary>
        /// <param name="engineCacheDirectory">Path to the EngineCache directory for this build configuration.</param>
        /// <param name="sentinelFilePath">
        /// Path to the sentinel file. If null, the LocalApplicationData default is used.
        /// If LocalApplicationData is unavailable, the sentinel is disabled.
        /// </param>
        internal BuildSentinel(string engineCacheDirectory, string sentinelFilePath = null)
        {
            m_engineCacheDirectory = engineCacheDirectory;
            m_sentinelFilePath = sentinelFilePath ?? GetDefaultSentinelFilePath();
        }

        /// <summary>
        /// The full path to the sentinel file, or null if unavailable.
        /// </summary>
        public string SentinelFilePath => m_sentinelFilePath;

        /// <summary>
        /// Returns true if evidence of a prior BuildXL execution is found.
        /// Checks the sentinel file first (primary), then falls back to looking for
        /// top-level files in the EngineCache directory left by prior bxl versions.
        /// </summary>
        public bool HasPriorBuild()
        {
            if (m_sentinelFilePath != null && File.Exists(m_sentinelFilePath))
            {
                return true;
            }

            return HasEngineCacheFiles();
        }

        /// <summary>
        /// Writes the sentinel file to indicate that BuildXL has started on this machine.
        /// Always writes to LocalApplicationData so subsequent builds can detect it.
        /// Returns false if the sentinel could not be written due to I/O or permission errors.
        /// No-op (returns true) if the sentinel path is unavailable.
        /// </summary>
        public bool TryMarkBuildStarted()
        {
            if (m_sentinelFilePath == null)
            {
                return true;
            }

            try
            {
                var directory = Path.GetDirectoryName(m_sentinelFilePath);
                Directory.CreateDirectory(directory);
                File.Create(m_sentinelFilePath).Dispose();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks for any file directly in the EngineCache directory (top-level only).
        /// Uses SearchOption.TopDirectoryOnly to avoid traversing into junctions
        /// (e.g., BuildEngineDirectory) which could produce false positives.
        /// </summary>
        private bool HasEngineCacheFiles()
        {
            try
            {
                return Directory.Exists(m_engineCacheDirectory)
                    && Directory.EnumerateFiles(m_engineCacheDirectory, "*", SearchOption.TopDirectoryOnly).GetEnumerator().MoveNext();
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string GetDefaultSentinelFilePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                return null;
            }

            return Path.Combine(localAppData, SentinelDirectoryName, SentinelFileName);
        }
    }
}
