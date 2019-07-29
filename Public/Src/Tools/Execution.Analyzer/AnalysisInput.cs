// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Engine.Serialization;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Represents the inputs to an analysis (ie cached graph and execution log)
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals", Justification = "Not an issue")]
    public struct AnalysisInput
    {
        /// <summary>
        /// The cached graph
        /// </summary>
        public CachedGraph CachedGraph { get; private set; }

        internal string CachedGraphDirectory { get; private set; }

        internal FileSystemStreamProvider StreamProvider;

        private string m_executionLogPath;

        public string ExecutionLogPath
        {
            get
            {
                return m_executionLogPath;
            }

            set
            {
                m_executionLogPath = ProcessExecutionLogPath(value);
            }
        }


        internal string ProcessExecutionLogPath(string path)
        {
            if (path != null && path.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                var zipStreamProvider = new ZipFileSystemStreamProvider(path);
                StreamProvider = zipStreamProvider;
                using (var archiveWrapper = zipStreamProvider.OpenArchiveForRead())
                {
                    var archive = archiveWrapper.Value;
                    var xlgEntries = archive.Entries.Where(entry => entry.FullName.EndsWith(".xlg", System.StringComparison.OrdinalIgnoreCase)).ToList();
                    if (xlgEntries.Count != 1)
                    {
                        throw new BuildXLException(I($"Execution log path '{path}' appears to refer to a zip file. Expected to find exactly one entry with extension '.xlg' but found {xlgEntries.Count} "));
                    }

                    path = Path.Combine(path, xlgEntries[0].FullName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
                }
            }
            else
            {
                StreamProvider = FileSystemStreamProvider.Default;

                if (path != null && Directory.Exists(path))
                {
                    var xlgFiles = Directory.GetFiles(path, "*.xlg", SearchOption.TopDirectoryOnly);
                    if (xlgFiles.Length != 1)
                    {
                        throw new BuildXLException(I($"Execution log path '{path}' appears to refer to a directory. Expected to find exactly one file at root with extension '.xlg' but found {xlgFiles.Length} "));
                    }

                    path = xlgFiles[0];
                }
            }

            return path;
        }

        public bool LoadCacheGraph(string cachedGraphDirectory)
        {
            if (string.IsNullOrEmpty(cachedGraphDirectory) && string.IsNullOrEmpty(ExecutionLogPath))
            {
                return false;
            }

            // Dummy logger that nothing listens to but is needed for cached graph API
            LoggingContext loggingContext = new LoggingContext("BuildXL.Execution.Analyzer");

            CachedGraphDirectory = !string.IsNullOrWhiteSpace(cachedGraphDirectory)
                ? cachedGraphDirectory
                : Path.Combine(Path.GetDirectoryName(ExecutionLogPath), Path.GetFileNameWithoutExtension(ExecutionLogPath));

            CachedGraph = CachedGraph.LoadAsync(CachedGraphDirectory, loggingContext, preferLoadingEngineCacheInMemory: true, readStreamProvider: StreamProvider).GetAwaiter().GetResult();

            if (CachedGraph == null)
            {
                return false;
            }

            return true;
        }

        public bool ReadExecutionLog(IExecutionLogTarget analyzer)
        {
            using (var disposableLogReader = LoadExecutionLog(analyzer))
            {
                if (disposableLogReader?.Value != null)
                {
                    return disposableLogReader.Value.ReadAllEvents();
                }
            }

            return false;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private Disposable<ExecutionLogFileReader> LoadExecutionLog(IExecutionLogTarget analyzer)
        {
            Contract.Requires(CachedGraph != null);

            if (ExecutionLogPath != null)
            {
                var analysisInput = this;
                var readerWrapper = StreamProvider.OpenReadStream(ExecutionLogPath)
                    .ChainSelect(executionLogStream => new ExecutionLogFileReader(executionLogStream, analysisInput.CachedGraph.Context, analyzer, closeStreamOnDispose: true));

                var reader = readerWrapper.Value;

                if (!reader.LogId.HasValue)
                {
                    throw CommandLineUtilities.Error("Could not load execution log. Execution log does not have header containing pip graph id.");
                }

                if (reader.LogId.Value != CachedGraph.PipGraph.GraphId)
                {
                    throw CommandLineUtilities.Error("Could not load execution log. Execution log header contains pip graph id that does not match id from loaded pip graph.");
                }

                return readerWrapper;
            }

            return null;
        }
    }
}
