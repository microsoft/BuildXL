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
using ZstdSharp;
using static BuildXL.Utilities.Core.FormattableStringEx;

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

        /// <summary>
        /// Environment variable used to override the path where the decompressed execution log is stored. Used for testing
        /// </summary>
        internal const string BuildXLAnalyzerWorkingDirEnvVar = "BuildXLAnalyzerWorkingDir";

        /// <summary>
        /// Magic number used to indicate zstd compression
        /// </summary>
        private static readonly byte[] s_zstdMagicNumber = [0x28, 0xB5, 0x2F, 0xFD];

        internal string ProcessExecutionLogPath(string path)
        {
            if (path != null && path.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                // TODO - It doesn't seem there are any codepaths where BuildXL itself writes out an execution log in a zip file, so this code is likely not currently used.
                // Leaving the code in case Office or some consumer has logic to zip files post-build
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

                    // Zip archives can contain relative paths that allow files to escape the zip file's directory.
                    // Validate the path doesn't excape before using it
                    var zipDirectory  = Path.GetDirectoryName(path);
                    path = Path.Combine(path, xlgEntries[0].FullName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));

                    if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(zipDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new BuildXLException(I($"Entry '{xlgEntries[0].FullName}' in zip file '{path}' escapes the containing directory and is not allowed."));
                    }
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

            var streamProvider = StreamProvider;
            if (cachedGraphDirectory != null && cachedGraphDirectory.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase) && File.Exists(cachedGraphDirectory))
            {
                streamProvider = new ZipFileSystemStreamProvider(cachedGraphDirectory);
            }

            // Dummy logger that nothing listens to but is needed for cached graph API
            LoggingContext loggingContext = new LoggingContext("BuildXL.Execution.Analyzer");

            CachedGraphDirectory = !string.IsNullOrWhiteSpace(cachedGraphDirectory)
                ? cachedGraphDirectory
                : Path.Combine(Path.GetDirectoryName(ExecutionLogPath), Path.GetFileNameWithoutExtension(ExecutionLogPath));

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
                    .ChainSelect(executionLogStream => new ExecutionLogFileReader(SwapForDecompressedStream(executionLogStream, out _), analysisInput.CachedGraph.Context, analyzer, closeStreamOnDispose: true));

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

        /// <summary>
        /// Returns a decompressed stream if the input stream is compressed, otherwise returns the original stream
        /// </summary>
        /// <remarks>
        /// If the input stream is compressed, this function will dispose it prior to returning an uncompressed stream. The caller
        /// is always responsible for disposing the returned stream.
        /// 
        /// This strategy is more complicated than simply using a <see cref="DecompressionStream"/> which will decompress on the fly
        /// as the stream is read. The reason for this is that the <see cref="DecompressionStream"/> does not support seeking, which
        /// is used by the <see cref="ExecutionLogFileReader"/>.
        /// </remarks>
        /// <param name="inputStream">Possibly zstd compressed execution log stream. If compressed, this stream will be disposed prior to this function returning</param>
        /// <param name="usedCachedStream">Whether or not the returned stream is a cached stream (for testing)</param>
        /// <returns>Uncompressed stream. The caller is responsible for disposing this stream.</returns>
        internal static Stream SwapForDecompressedStream(Stream inputStream, out bool usedCachedStream)
        {
            usedCachedStream = false;
            if (!IsStreamZstdCompressed(inputStream))
            {
                // The stream was not compressed. Use the original stream
                return inputStream;
            }
            else
            {
                // The file is compressed. Decompress it to a file under the temp directory and return that stream instead.
                // As an optimization, the decompressed file is retained in the temp directory and re-used by subsequent runs
                // if the first part of the input stream matches the existing file.
                string workingDirOverride = Environment.GetEnvironmentVariable(BuildXLAnalyzerWorkingDirEnvVar);
                string analyzerWorkingDir = Path.Combine(!string.IsNullOrWhiteSpace(workingDirOverride) ? workingDirOverride : Path.GetTempPath(), "BuildXLAnalyzerWorkingDir");
                string decompressedPath = Path.Combine(analyzerWorkingDir, "decompressed.xlg");

                Directory.CreateDirectory(analyzerWorkingDir);

                // Get a handle to the file to write the decompressed stream to
                FileStream decompressedFile;
                try
                {
                    decompressedFile = File.Open(decompressedPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    // Most likely the file is in use by a concurrent analyzer run.
                    // Try again with a unique suffix, flagging the file to be deleted on close to avoid accumulation
                    decompressedPath = decompressedPath + Guid.NewGuid().ToString("N");
                    decompressedFile = File.Open(Path.Combine(decompressedPath), new FileStreamOptions() { Options = FileOptions.DeleteOnClose, Mode = FileMode.CreateNew, Share = FileShare.Delete, Access = FileAccess.ReadWrite });
                }

                using (var decompressionStream = new DecompressionStream(inputStream, leaveOpen: false))
                {
                    // Hash the first 1MB of the stream to see if we can reuse what's already in the temp directory
                    // Execution log events include timestamp offsets so having exactly the same content between different
                    // execution logs beyond the first 1MB would be extremely unlikely
                    const int BytesToCompare = 1028 * 1028;
                    
                    var decompressedInputStreamBytes = readComparisonBytes(decompressionStream, BytesToCompare);
                    var existingDecompressedFileBytes = readComparisonBytes(decompressedFile, BytesToCompare);

                    if (decompressedInputStreamBytes.bytes.AsSpan().SequenceEqual(existingDecompressedFileBytes.bytes.AsSpan()))
                    {
                        // The files are the same. Reuse the existing one unmodified.
                        usedCachedStream = true;
                    }
                    else
                    {
                        // The the pre-existing file is not the same as the current stream to decompress.
                        // Truncate and then write out the first decompressed comparison bytes followed by the rest of the decompressed stream
                        decompressedFile.SetLength(0);
                        decompressedFile.Write(decompressedInputStreamBytes.bytes, 0, decompressedInputStreamBytes.bytesRead);
                        decompressionStream.CopyTo(decompressedFile);
                    }
                }

                // Reset the position of the returned stream to make it ready for the reader.
                decompressedFile.Position = 0;
                return decompressedFile;


                (byte[] bytes, int bytesRead) readComparisonBytes(Stream stream, int bytesToRead)
                {
                    byte[] bytes = new byte[bytesToRead];
                    int readBytes = 0;
                    while (readBytes < bytes.Length)
                    {
                        int bytesInBatch = stream.Read(bytes, readBytes, bytes.Length - readBytes);
                        if (bytesInBatch == 0)
                        {
                            break;
                        }
                        readBytes += bytesInBatch;
                    }

                    return (bytes, readBytes);
                }
            }
        }

        /// <summary>
        /// Determines whether the stream is zstd compressed.
        /// </summary>
        private static bool IsStreamZstdCompressed(Stream seekableStream)
        {
            Contract.AssertNotNull(seekableStream);
            Contract.Assert(seekableStream.CanSeek);

            if (seekableStream.Length < 4)
            {
                return false;
            }

            // Read out the first 4 bytes to look for the zstd magic number
            seekableStream.Position = 0;
            var possibleMagicNumber = new byte[4];
            seekableStream.Read(possibleMagicNumber, 0, 4);

            // Reset the stream
            seekableStream.Position = 0;

            return possibleMagicNumber.SequenceEqual(s_zstdMagicNumber);
        }
    }
}
