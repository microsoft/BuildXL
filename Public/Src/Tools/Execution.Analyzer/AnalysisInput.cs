// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Engine.Serialization;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.Core.FormattableStringEx;
using ExecutionLogDecompressorConstants = BuildXL.Execution.Analyzer.ExecutionLogDecompressor.ExecutionLogDecompressorConstants;

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
                    VerifyNumberOfXlgFiles(xlgEntries.Count, path, "zip");
                    path = Path.Combine(path, xlgEntries[0].FullName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
                }
            }
            else
            {
                StreamProvider = FileSystemStreamProvider.Default;

                if (path != null && Directory.Exists(path))
                {
                    var xlgFiles = Directory.GetFiles(path, "*.xlg", SearchOption.TopDirectoryOnly);
                    VerifyNumberOfXlgFiles(xlgFiles.Length, path, "directory");
                    path = xlgFiles[0];
                }
            }

            // Validate and decompress the compressed ExecutionLogFile.
            return ExecutionLogDecompressor.GetDecompressedExecutionLogFile(path);
        }

        /// <summary>
        /// Ensure that there are not more than two xlg files at any time in the logs folder.
        /// If the file is compressed then there will be two xlg files.
        /// Ex: /Out/Logs/BuildXL.xlg and /Out/Logs/BuildXL_decompressed.xlg
        /// </summary>
        private void VerifyNumberOfXlgFiles(int fileCount, string path, string type)
        {
            if (fileCount < 1 || fileCount > 2)
            {
                throw new BuildXLException(I($"Execution log path '{path}' appears to refer to a {type}. Expected to find not more than two entries with extension '.xlg' but found {fileCount} "));
            }
        }

        public bool LoadCacheGraph(string cachedGraphDirectory)
        {
            if (string.IsNullOrEmpty(cachedGraphDirectory) && string.IsNullOrEmpty(ExecutionLogPath))
            {
                return false;
            }

            var streamProvider = StreamProvider;
            if (cachedGraphDirectory != null && cachedGraphDirectory.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase) && File.Exists(cachedGraphDirectory))
            {
                streamProvider = new ZipFileSystemStreamProvider(cachedGraphDirectory);
            }

            // Dummy logger that nothing listens to but is needed for cached graph API
            LoggingContext loggingContext = new LoggingContext("BuildXL.Execution.Analyzer");

            CachedGraphDirectory = !string.IsNullOrWhiteSpace(cachedGraphDirectory)
                ? cachedGraphDirectory
               : Path.Combine(
                    Path.GetDirectoryName(ExecutionLogPath),
                    // We need to extract the suffix from the decompressed file name of the format BuildXL_decompressed.xlg
                    Path.GetFileName(ExecutionLogPath).Contains(ExecutionLogDecompressorConstants.DecompressedExecutionLogFileSuffixWithExtension)
                        ? Path.GetFileName(ExecutionLogPath).Split(ExecutionLogDecompressorConstants.DecompressedExecutionLogFileSuffixWithExtension)[0]
                        : Path.GetFileNameWithoutExtension(ExecutionLogPath));

            using (ConsoleEventListener listener = new ConsoleEventListener(Events.Log, DateTime.UtcNow,
                eventMask: new EventMask(
                    enabledEvents: null, 
                    disabledEvents: new int[] 
                    { 
                        (int)BuildXL.Engine.Tracing.LogEventId.DeserializedFile, // Don't log anything for success
                    })
                ))
            {
                listener.RegisterEventSource(BuildXL.Engine.ETWLogger.Log);
                CachedGraph = CachedGraph.LoadAsync(CachedGraphDirectory, loggingContext, preferLoadingEngineCacheInMemory: true, readStreamProvider: streamProvider).GetAwaiter().GetResult();
            }
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
