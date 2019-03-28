// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Core.Tracing;
using BuildXL.FrontEnd.Sdk;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Core.Incrementality
{
    /// <summary>
    /// Simple cache implementation for different front-end artifacts.
    /// </summary>
    internal sealed class FrontEndCache
    {
#pragma warning disable CA1823 // Version is used in a local function, and stylecop can't see this usage
        /// <summary>
        /// Serialization version. Increment this when the serialized format changes
        /// </summary>
        private const int Version = 1;
#pragma warning restore CA1823 // Version is used in a local function, and stylecop can't see this usage

        private readonly Logger m_logger;
        private readonly LoggingContext m_loggingContext;
        private readonly IFrontEndStatistics m_frontEndStatistics;
        private readonly PathTable m_pathTable;
        private readonly string m_cacheFileName;

        /// <summary>
        /// File name of the front end cache.
        /// </summary>
        public const string CacheFileName = "FrontEndCache";

        /// <nodoc />
        public FrontEndCache(string outputFolder, Logger logger, LoggingContext loggingContext, IFrontEndStatistics frontEndStatistics, PathTable pathTable)
        {
            m_logger = logger;
            m_loggingContext = loggingContext;
            m_frontEndStatistics = frontEndStatistics;
            m_pathTable = pathTable;
            m_cacheFileName = Path.Combine(outputFolder, CacheFileName);
        }

        [CanBeNull]
        public WorkspaceBindingSnapshot TryLoadFrontEndSnapshot(int expectedSpecCount)
        {
            try
            {
                return ExceptionUtilities.HandleRecoverableIOException(
                    () => DoLoadFrontEndSnapshot(),
                    e => throw new BuildXLException(string.Empty, e));
            }
            catch (BuildXLException e)
            {
                // Recoverable exceptions should not break BuildXL.
                m_logger.FailToReuseFrontEndSnapshot(m_loggingContext, I($"IO exception occurred: {e.InnerException}"));
                return null;
            }

            WorkspaceBindingSnapshot DoLoadFrontEndSnapshot()
            {
                if (!File.Exists(m_cacheFileName))
                {
                    // Can't reuse the snapshot because the file does not exist.
                    m_logger.FailToReuseFrontEndSnapshot(m_loggingContext, I($"File '{m_cacheFileName}' does not exist"));
                    return null;
                }

                var sw = Stopwatch.StartNew();
                using (var file = FileUtilities.CreateFileStream(m_cacheFileName, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
                {
                    var reader = new BuildXLReader(debug: false, stream: file, leaveOpen: true);
                    var version = reader.ReadInt32Compact();
                    if (version != Version)
                    {
                        // Version mismatch. Can't reuse the file.
                        m_logger.FailToReuseFrontEndSnapshot(
                            m_loggingContext,
                            I($"Cache version '{version}' does not match an expected version '{Version}'"));
                        return null;
                    }

                    var specCount = reader.ReadInt32Compact();
                    if (expectedSpecCount != specCount)
                    {
                        // Can't use the cache, because it has different amount of specs in there.
                        m_logger.FailToReuseFrontEndSnapshot(
                            m_loggingContext,
                            I($"Cache contains the data for '{specCount}' specs but current execution requires '{expectedSpecCount}' specs"));
                        return null;
                    }

                    var specs = FrontEndSnapshotSerializer.DeserializeSpecStates(reader, m_pathTable, specCount);

                    m_frontEndStatistics.FrontEndSnapshotLoadingDuration = sw.Elapsed;

                    m_logger.LoadFrontEndSnapshot(m_loggingContext, specs.Length, (int)sw.ElapsedMilliseconds);
                    return new WorkspaceBindingSnapshot(specs, m_pathTable);
                }
            }
        }

        /// <nodoc />
        public void SaveFrontEndSnapshot(IWorkspaceBindingSnapshot snapshot)
        {
            try
            {
                ExceptionUtilities.HandleRecoverableIOException(
                    () => DoSaveFrontEndSnapshot(),
                    e => throw new BuildXLException(string.Empty, e));
            }
            catch (BuildXLException e)
            {
                m_logger.SaveFrontEndSnapshotError(m_loggingContext, m_cacheFileName, e.InnerException.ToString());
            }

            void DoSaveFrontEndSnapshot()
            {
                var sw = Stopwatch.StartNew();

                using (var file = FileUtilities.CreateFileStream(m_cacheFileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Delete))
                {
                    var writer = new BuildXLWriter(debug: false, stream: file, leaveOpen: true, logStats: false);
                    writer.WriteCompact(Version);

                    FrontEndSnapshotSerializer.SerializeWorkspaceBindingSnapshot(snapshot, writer, m_pathTable);
                }

                m_frontEndStatistics.FrontEndSnapshotSavingDuration = sw.Elapsed;
                m_logger.SaveFrontEndSnapshot(m_loggingContext, m_cacheFileName, (int)sw.ElapsedMilliseconds);
            }
        }
    }
}
