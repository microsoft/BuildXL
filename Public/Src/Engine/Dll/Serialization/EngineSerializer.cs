// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Serialization;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Engine
{
    /// <summary>
    /// Helper saving and loading engine state between runs
    /// </summary>
    public sealed class EngineSerializer
    {
        #region FileNames

        internal const string PathTableFile = "PathTable";
        internal const string StringTableFile = "StringTable";
        internal const string SymbolTableFile = "SymbolTable";
        internal const string QualifierTableFile = "QualifierTable";
        internal const string PipTableFile = "PipTable";
        internal const string PreviousInputsFile = "PreviousInputs";
        internal const string PreviousInputsJournalCheckpointFile = "PreviousInputsJournalCheckpoint";
        internal const string PreviousInputsIntermediateFile = "PreviousInputs.tmp";
        internal const string MountPathExpanderFile = "MountPathExpander";
        internal const string ConfigFileStateFile = "ConfigFileState";
        internal const string RunningTimeTableFile = "RunningTimeTable";
        internal const string DirectedGraphFile = "DirectedGraph";
        internal const string PipGraphFile = "PipGraph";
        internal const string PipGraphIdFile = "PipGraphId";
        internal const string HistoricTableSizes = "HistoricTableSizes";
        internal const string SchedulerDirtyNodesCheckpointFile = "SchedulerDirtyNodesCheckpoint";
        internal const string SchedulerJournalCheckpointFile = "SchedulerJournalCheckpoint";
        internal const string EngineStateFile = "EngineState";
        internal const string HistoricMetadataCacheLocation = "HistoricMetadataCache";
        internal const string CorruptFilesLogLocation = "Error-CorruptState";

        #endregion

        private readonly string m_engineCacheLocation;
        private readonly bool m_debug;
        private readonly List<Task<SerializationResult>> m_serializationTasks = new List<Task<SerializationResult>>();
        private readonly List<Task> m_deserializationTasks = new List<Task>();
        private readonly object m_deserializationSyncObject = new object();
        private long m_bytesDeserialized;
        private long m_bytesSerialized;
        private long m_bytesSavedDueToCompression;
        private object m_correlationId;
        private readonly bool m_useCompression;
        private readonly FileSystemStreamProvider m_readStreamProvider;
        private readonly ITempDirectoryCleaner m_tempDirectoryCleaner;

        /// <summary>
        /// Constructor
        /// </summary>
        public EngineSerializer(
            LoggingContext loggingContext,
            string engineCacheLocation,
            FileEnvelopeId? correlationId = null,
            bool useCompression = false,
            bool debug = false,
            bool readOnly = false,
            FileSystemStreamProvider readStreamProvider = null,
            ITempDirectoryCleaner tempDirectoryCleaner = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(engineCacheLocation != null);
            Contract.Requires(Path.IsPathRooted(engineCacheLocation));
            Contract.Requires(!string.IsNullOrWhiteSpace(engineCacheLocation));

            LoggingContext = loggingContext;
            m_engineCacheLocation = engineCacheLocation;
            m_debug = debug;
            m_correlationId = correlationId;
            m_useCompression = useCompression;
            m_readStreamProvider = readStreamProvider ?? FileSystemStreamProvider.Default;
            m_tempDirectoryCleaner = tempDirectoryCleaner;

            if (!readOnly)
            {
                try
                {
                    FileUtilities.CreateDirectoryWithRetry(engineCacheLocation);
                }
                catch (Exception ex)
                {
                    ExceptionRootCause rootCause = ExceptionUtilities.AnalyzeExceptionRootCause(ex);
                    BuildXL.Tracing.Logger.Log.UnexpectedCondition(LoggingContext, ex.ToStringDemystified()  + Environment.NewLine + rootCause);
                    throw new BuildXLException("Unable to create engine serializer cache directory: ", ex);
                }
            }
        }

        internal char EngineCacheDriveLetter => m_engineCacheLocation[0];

        internal string PreviousInputsIntermediate => Path.Combine(m_engineCacheLocation, PreviousInputsIntermediateFile);

        internal string PreviousInputsFinalized => Path.Combine(m_engineCacheLocation, PreviousInputsFile);

        internal string PreviousInputsJournalCheckpoint => Path.Combine(m_engineCacheLocation, PreviousInputsJournalCheckpointFile);

        /// <summary>
        /// Count of total bytes deserialized
        /// </summary>
        internal long BytesDeserialized => Volatile.Read(ref m_bytesDeserialized);

        /// <summary>
        /// Count of total bytes serialized
        /// </summary>
        internal long BytesSerialized => Volatile.Read(ref m_bytesSerialized);

        /// <summary>
        /// Count of total bytes saved due to compression
        /// </summary>
        internal long BytesSavedDueToCompression => Volatile.Read(ref m_bytesSavedDueToCompression);

        /// <summary>
        /// The logging context used during serialization
        /// </summary>
        internal LoggingContext LoggingContext { get; }

        /// <summary>
        /// Moves the PreviousInputs file from the temp location to its final location.
        /// </summary>
        internal bool FinalizePreviousInputsFile()
        {
            try
            {
                if (!File.Exists(PreviousInputsIntermediate))
                {
                    Contract.Assume(false, "Intermediate PreviousInputs file did not exist at " + PreviousInputsIntermediate);
                }

                FileUtilities.MoveFileAsync(PreviousInputsIntermediate, PreviousInputsFinalized, replaceExisting: true).Wait();
                return true;
            }
            catch (BuildXLException)
            {
                return false;
            }
        }

        /// <summary>
        /// Tries to delete previous input journal checkpoint file.
        /// </summary>
        internal bool TryDeletePreviousInputsJournalCheckpointFile()
        {
            return FileUtilities.TryDeleteFile(PreviousInputsJournalCheckpoint, waitUntilDeletionFinished: true, tempDirectoryCleaner: m_tempDirectoryCleaner).Succeeded;
        }

        internal IList<Task<SerializationResult>> SerializationTasks => m_serializationTasks;

        internal async Task WaitForPendingDeserializationsAsync()
        {
            int index = 0;
            while (true)
            {
                Task deserializationTask;

                lock (m_deserializationSyncObject)
                {
                    var count = m_deserializationTasks.Count;

                    if (index >= count)
                    {
                        break;
                    }

                    deserializationTask = m_deserializationTasks[index];
                }

                await deserializationTask;

                index++;
            }
        }

        /// <summary>
        /// Gets the full path to a serializable file given its label
        /// </summary>
        internal string GetFullPath(string objectLabel)
        {
            return Path.Combine(m_engineCacheLocation, objectLabel);
        }

        /// <summary>
        /// Gets the full path to a serializable file given its file type
        /// </summary>
        internal string GetFullPath(GraphCacheFile fileType)
        {
            return Path.Combine(m_engineCacheLocation, GetFileName(fileType));
        }

        /// <summary>
        /// Creates and starts a task to deserialize an object
        /// </summary>
        /// <param name="file">This will become the filename</param>
        /// <param name="deserializer">Deserialization function; its get a reader for the file stream, and a function that allows obtaining additional streams if needed</param>
        /// <param name="skipHeader">If enabled, the correlation id is not checked for consistency</param>
        /// <returns>task for deserialized value</returns>
        internal Task<TObject> DeserializeFromFileAsync<TObject>(
            GraphCacheFile file,
            Func<BuildXLReader, Task<TObject>> deserializer,
            bool skipHeader = false)
        {
            var task = Task.Run(
                async () =>
                {
                    var objectLabel = GetFileName(file);
                    string path = GetFullPath(objectLabel);
                    FileEnvelope fileEnvelope = GetFileEnvelope(file);

                    var result = default(TObject);

                    try
                    {
                        Stopwatch sw = Stopwatch.StartNew();

                        using (var fileStreamWrapper = m_readStreamProvider.OpenReadStream(path))
                        {
                            var fileStream = fileStreamWrapper.Value;

                            FileEnvelopeId persistedCorrelationId = fileEnvelope.ReadHeader(fileStream);

                            if (!skipHeader)
                            {
                                // We are going to check if all files that are going to be (concurrently) deserialized have matching correlation ids.
                                // The first discovered correlation id is going to be used to check all others.
                                if (m_correlationId == null)
                                {
                                    Interlocked.CompareExchange(ref m_correlationId, persistedCorrelationId, null);
                                }

                                FileEnvelope.CheckCorrelationIds(persistedCorrelationId, (FileEnvelopeId)m_correlationId);
                            }

                            var isCompressed = fileStream.ReadByte() == 1;

                            using (Stream readStream = isCompressed ? new DeflateStream(fileStream, CompressionMode.Decompress) : fileStream)
                            using (BuildXLReader reader = new BuildXLReader(m_debug, readStream, leaveOpen: false))
                            {
                                result = await deserializer(reader);
                            }
                        }

                        Tracing.Logger.Log.DeserializedFile(LoggingContext, path, sw.ElapsedMilliseconds);
                        return result;
                    }
                    catch (BuildXLException ex)
                    {
                        if (ex.InnerException is FileNotFoundException)
                        {
                            // Files might be deleted manually in the EngineCache directory. Log it as verbose.
                            Tracing.Logger.Log.FailedToDeserializeDueToFileNotFound(LoggingContext, path);
                            return result;
                        }

                        Tracing.Logger.Log.FailedToDeserializePipGraph(LoggingContext, path, ex.LogEventMessage);
                        return result;
                    }
                    catch (IOException ex)
                    {
                        Tracing.Logger.Log.FailedToDeserializePipGraph(LoggingContext, path, ex.Message);
                        return result;
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // There are 2 reasons to be here.
                        //    1. A malformed file can cause ContractException, IndexOutOfRangeException, MemoryException or something else.
                        //    2. We may have a bug.
                        // Since the malformed file will always cause a crash until someone removes the file from the cache, allow BuildXL to recover
                        // by eating the exception. However remember to log it in order to keep track of bugs.
                        ExceptionRootCause rootCause = ExceptionUtilities.AnalyzeExceptionRootCause(ex);
                        BuildXL.Tracing.Logger.Log.UnexpectedCondition(LoggingContext, ex.ToStringDemystified() + Environment.NewLine + rootCause);
                        Tracing.Logger.Log.FailedToDeserializePipGraph(LoggingContext, path, ex.Message);
                        return result;
                    }
                });

            lock (m_deserializationSyncObject)
            {
                m_deserializationTasks.Add(task);
            }

            return task;
        }

        /// <summary>
        /// Creates and starts a task to serialize an object.
        /// </summary>
        /// <param name="fileType">Type for the object to serialize. This will become the filename</param>
        /// <param name="serializer">Serialization action to perform</param>
        /// <param name="overrideName">Overrides the default file name for the file type. This is used to atomically write some files (via renames)</param>
        /// <returns>whether serialization was successful</returns>
        public Task<SerializationResult> SerializeToFileAsync(
            GraphCacheFile fileType,
            Action<BuildXLWriter> serializer,
            string overrideName = null)
        {
            var task = SerializeToFileInternal(fileType, serializer, overrideName);
            SerializationTasks.Add(task);
            return task;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        private async Task<SerializationResult> SerializeToFileInternal(GraphCacheFile fileType, Action<BuildXLWriter> serializer, string overrideName)
        {
            // Unblock the caller
            await Task.Yield();

            FileUtilities.CreateDirectory(m_engineCacheLocation);

            string fileName = overrideName ?? GetFileName(fileType);
            string path = Path.Combine(m_engineCacheLocation, fileName);
            SerializationResult serializationResult = new SerializationResult()
            {
                Success = false,
                FileType = fileType,
                FullPath = path,
            };

            var fileEnvelope = GetFileEnvelope(fileType);
            Contract.Assume(m_correlationId is FileEnvelopeId, "EngineSerializer must be initialized with a valid correlation id");
            var correlationId = (FileEnvelopeId)m_correlationId;

            try
            {
                // We must delete the existing file in case it was hardlinked from the cache. Opening a filestream
                // that truncates the existing file will fail if it is a hardlink.
                FileUtilities.DeleteFile(path, tempDirectoryCleaner: m_tempDirectoryCleaner);
                using (
                    FileStream fileStream = FileUtilities.CreateFileStream(
                        path,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Delete,
                        // Do not write the file with SequentialScan since it will be reread in the subsequent build
                        FileOptions.None))
                {
                    Stopwatch sw = Stopwatch.StartNew();

                    fileEnvelope.WriteHeader(fileStream, correlationId);

                    // Write whether the file is compressed or not.
                    fileStream.WriteByte(m_useCompression ? (byte)1 : (byte)0);

                    long uncompressedLength = 0;

                    if (m_useCompression)
                    {
                        using (var writer = new BuildXLWriter(m_debug, new TrackedStream(new DeflateStream(fileStream, CompressionLevel.Fastest, leaveOpen: true)), false, false))
                        {
                            // TODO: We can improve performance significantly by parallelizing the compression.
                            // There's no setting to do that, but given you have the entire file content upfront in a memory stream,
                            // it shouldn't be particularly complicated to split the memory stream into reasonably sized chunks (say 100MB)
                            // and compress each of them into separate MemoryStream backed DeflateStreams in separate threads.
                            // Then just write those out to a file. Of course you'll need to write out the position of those streams
                            // into the header when you write out the actual file.
                            serializer(writer);
                            uncompressedLength = writer.BaseStream.Length;
                        }
                    }
                    else
                    {
                        using (var writer = new BuildXLWriter(m_debug, fileStream, leaveOpen: true, logStats: false))
                        {
                            serializer(writer);
                            uncompressedLength = writer.BaseStream.Length;
                        }
                    }

                    Interlocked.Add(ref m_bytesSavedDueToCompression, uncompressedLength - fileStream.Length);

                    fileEnvelope.FixUpHeader(fileStream, correlationId);

                    Tracing.Logger.Log.SerializedFile(LoggingContext, fileName, sw.ElapsedMilliseconds);
                    serializationResult.Success = true;
                    Interlocked.Add(ref m_bytesSerialized, fileStream.Position);
                }
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.FailedToSerializePipGraph(LoggingContext, ex.LogEventMessage);
            }
            catch (IOException ex)
            {
                Tracing.Logger.Log.FailedToSerializePipGraph(LoggingContext, ex.Message);
            }

            return serializationResult;
        }

        private static FileEnvelope GetFileEnvelope(GraphCacheFile fileType)
        {
            switch (fileType)
            {
                case GraphCacheFile.PreviousInputs:
                    return InputTracker.FileEnvelope;
                case GraphCacheFile.PipTable:
                    return PipTable.FileEnvelope;
                case GraphCacheFile.PathTable:
                    return PathTable.FileEnvelope;
                case GraphCacheFile.StringTable:
                    return StringTable.FileEnvelope;
                case GraphCacheFile.SymbolTable:
                    return SymbolTable.FileEnvelope;
                case GraphCacheFile.QualifierTable:
                    return QualifierTable.FileEnvelope;
                case GraphCacheFile.MountPathExpander:
                    return MountPathExpander.FileEnvelope;
                case GraphCacheFile.ConfigState:
                    return ConfigFileState.FileEnvelope;
                case GraphCacheFile.DirectedGraph:
                    return DirectedGraph.FileEnvelope;
                case GraphCacheFile.PipGraph:
                    return PipGraph.FileEnvelopeGraph;
                case GraphCacheFile.PipGraphId:
                    return PipGraph.FileEnvelopeGraphId;
                case GraphCacheFile.HistoricTableSizes:
                    return EngineContext.HistoricTableSizesFileEnvelope;
                default:
                    throw Contract.AssertFailure("Unhandled GraphCacheFile");
            }
        }

        private static string GetFileName(GraphCacheFile fileType)
        {
            switch (fileType)
            {
                case GraphCacheFile.PreviousInputs:
                    return PreviousInputsFile;
                case GraphCacheFile.PipTable:
                    return PipTableFile;
                case GraphCacheFile.PathTable:
                    return PathTableFile;
                case GraphCacheFile.StringTable:
                    return StringTableFile;
                case GraphCacheFile.SymbolTable:
                    return SymbolTableFile;
                case GraphCacheFile.QualifierTable:
                    return QualifierTableFile;
                case GraphCacheFile.MountPathExpander:
                    return MountPathExpanderFile;
                case GraphCacheFile.ConfigState:
                    return ConfigFileStateFile;
                case GraphCacheFile.DirectedGraph:
                    return DirectedGraphFile;
                case GraphCacheFile.PipGraph:
                    return PipGraphFile;
                case GraphCacheFile.PipGraphId:
                    return PipGraphIdFile;
                case GraphCacheFile.HistoricTableSizes:
                    return HistoricTableSizes;
                default:
                    throw Contract.AssertFailure("Unhandled GraphCacheFile");
            }
        }

        /// <summary>
        /// When a corruption is detected, moves the engine state into the logs directory for future debugging
        /// and to prevent the corrupt state from impacting future builds.
        /// </summary>
        public static bool TryLogAndRemoveCorruptEngineState(IConfiguration configuration, PathTable pathTable, LoggingContext loggingContext)
        {
            var engineCache = configuration.Layout.EngineCacheDirectory.ToString(pathTable);
            var fileContentTableFile = configuration.Layout.FileContentTableFile.ToString(pathTable);

            bool success = true;
            // Non-recursive directory enumeration to prevent deleting folders which are persisted build-over-build in the engine cache 
            // but are not engine state (ex: HistoricMetadatCache and FingerprintStore)
            FileUtilities.EnumerateDirectoryEntries(engineCache, (fileName, attributes) =>
            {
                if (!FileUtilities.IsDirectoryNoFollow(attributes))
                {
                    var filePath = Path.Combine(engineCache, fileName);
                    success &= SchedulerUtilities.TryLogAndMaybeRemoveCorruptFile(
                        filePath, 
                        configuration, 
                        pathTable, 
                        loggingContext,
                        removeFile: filePath != fileContentTableFile /* exclude the file content table which can impact performance significantly if deleted */);
                }
            });

            return success;
        }

        /// <summary>
        /// Result from serialization
        /// </summary>
        public struct SerializationResult
        {
            /// <summary>
            /// Whether serialization was successful
            /// </summary>
            public bool Success;

            /// <summary>
            /// Type of file serialized, as understood by the underlying graph-cache.
            /// </summary>
            public GraphCacheFile FileType;

            /// <summary>
            /// Full path of file written to disk
            /// </summary>
            public string FullPath;
        }
    }
}
