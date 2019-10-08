// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Storage
{
    /// <summary>
    /// Combines files into a single large file for better read performance.
    /// </summary>
    /// <remarks>
    /// Only files loaded from disk may be retrieved from this object. So a file may not be a hit if it is added and then
    /// retrieved in the same lifetime of a FileCombiner which hasn't flushed to disk.
    /// </remarks>
    public sealed class FileCombiner : IDisposable
    {
        #region Consts

        /// <summary>
        /// Serialization version. Increment this when the serialized format changes
        /// </summary>
        private const int Version = 4;

        // Markers for whether the file was closed cleanly. These allow quickly aborting loading the structure if the
        // metadata is not valid
        private const int Dirty = 0x44495254; // DIRT
        private const int Clean = 0x54494459; // TIDY

        /// <summary>
        /// For simplicity, retrieving a file added in the same instance is not supported. This is a marker for a file
        /// that is registered but not available to be retrieved.
        /// </summary>
        private const int UnavailableFileMarker = -1;

        // Limit to reading 1 GB of combined file into memory in a byte array to avoid exceeding the maximum length of an array
        private const int DefaultMaxReadChunkBytes = 1024 * 1024 * 1024;

        /// <summary>
        /// Buffer for copying content from the stream.
        /// The buffer size is the largest multiple of 4096 that is still smaller than the large object heap threshold.
        /// </summary>
        private const int BufferSize = 81920;
        #endregion

        #region State

        /// <summary>
        /// The content of the file as read off of disk. Does not reflect any writes
        /// </summary>
        /// <remarks>
        /// The full file is read in at initialization for the sake of I/O speed at the expense of memory consumption.
        /// This approach may not be scalable for extrememly large projects and machines without large amounts of
        /// available memory.
        /// </remarks>
        private byte[][] m_content;

        /// <summary>
        /// The location of the next item. This is also where FileLocation metadata exists on initialization.
        /// </summary>
        private long m_nextItemLocation;

        /// <summary>
        /// Lookup of file path to index of FileLocation metadata
        /// </summary>
        /// <remarks>
        /// The metadata is held separately to keep it in the same order as how content is actually laid out in the
        /// backing file. This detail isn't presently used but may be leveraged to read in chunks of the backing file.
        /// </remarks>
        private readonly ConcurrentDictionary<string, int> m_filesByPath = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The FileLocation metadata read in during initialization
        /// </summary>
        private FileLocation[] m_fileLocations;

        /// <summary>
        /// FileLocation metadata for items added after initialization. These are not retrievable until the backing
        /// file is reloaded
        /// </summary>
        private readonly List<FileLocation> m_addedFileLocations = new List<FileLocation>();

        /// <summary>
        /// The use application of this FileCombiner. Used for logging
        /// </summary>
        private readonly FileCombinerUsage m_usage;

        private readonly Tracing.Logger.FileCombinerStats m_stats = new Tracing.Logger.FileCombinerStats();
        #endregion

        #region I/O

        /// <summary>
        /// Path of backing file
        /// </summary>
        private readonly string m_path;

        /// <summary>
        /// Stream to the backing file
        /// </summary>
        private FileStream m_file;

        /// <summary>
        /// Reader to file
        /// </summary>
        private BuildXLReader m_reader;

        /// <summary>
        /// Writer to file
        /// </summary>
        private BuildXLWriter m_writer;

        private readonly byte[] m_copyBuffer = new byte[BufferSize];
        #endregion

        /// <summary>
        /// Task that initializes the metadata
        /// </summary>
        private Task m_initializationTask;

        /// <summary>
        /// If initialization fails, the FileCombiner will become disabled and no-op.
        /// </summary>
        private bool m_enabled = true;

        private int m_hits;
        private int m_misses;
        private bool m_disposed;

        /// <summary>
        /// Index of where the first piece of file content is stored
        /// </summary>
        private long m_contentStartLocation;

        private readonly double m_allowableUnreferencedRatio;

        private readonly LoggingContext m_loggingContext;

        private readonly int m_maxReadChunkBytes;

        private readonly bool m_logFileCombinerStatistics;

        /// <summary>
        /// Metadata about where a file is stored
        /// </summary>
        private struct FileLocation : IEquatable<FileLocation>
        {
            /// <summary>
            /// Path of the file
            /// </summary>
            public string Path;

            /// <summary>
            /// Content hash of the file
            /// </summary>
            public ContentHash Hash;

            /// <summary>
            /// Length of the file
            /// </summary>
            public int Length;

            /// <summary>
            /// Whether the file was referenced
            /// </summary>
            /// <remarks>
            /// Note this does not get serialized since that state does not need to be persisted between runs.
            /// </remarks>
            public bool WasReferenced;

            /// <summary>
            /// Offset where the file starts
            /// </summary>
            public long Offset;

            /// <summary>
            /// Deserializes
            /// </summary>
            public static FileLocation Deserialize(BuildXLReader reader)
            {
                FileLocation result = default(FileLocation);
                result.Path = reader.ReadString();
                result.Hash = ContentHashingUtilities.CreateFrom(reader);
                result.Length = reader.ReadInt32Compact();
                result.Offset = reader.ReadInt64Compact();

                return result;
            }

            /// <summary>
            /// Serializes
            /// </summary>
            public void Serialize(BuildXLWriter writer)
            {
                writer.Write(Path);
                Hash.SerializeHashBytes(writer);
                writer.WriteCompact(Length);
                writer.WriteCompact(Offset);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return StructUtilities.Equals(this, obj);
            }

            /// <summary>
            /// Indicates whether two object instances are equal.
            /// </summary>
            public static bool operator ==(FileLocation left, FileLocation right)
            {
                return left.Equals(right);
            }

            /// <summary>
            /// Indicates whether two objects instances are not equal.
            /// </summary>
            public static bool operator !=(FileLocation left, FileLocation right)
            {
                return !left.Equals(right);
            }

            /// <summary>
            /// Whether a FileLocation equals this one
            /// </summary>
            public bool Equals(FileLocation other)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Info on how the FileCombiner is being used
        /// </summary>
        public enum FileCombinerUsage
        {
            /// <summary>
            /// Used for the SpecFileCache
            /// </summary>
            SpecFileCache,

            /// <summary>
            /// Used for DScript's incremental parse/evaluation
            /// </summary>
            IncrementalScriptFrontEnd,
        }

        /// <summary>
        /// Creates a new FileCombiner
        /// </summary>
        /// <param name="loggingContext">The LoggingContext</param>
        /// <param name="path">Path of the backing file</param>
        /// <param name="usage">Identifies the use application of the FileCombiner</param>
        /// <param name="logFileCombinerStatistics">This class logs a bunch of stats at dispose time. This flag controls wether this happens.</param>
        /// <param name="allowableUnreferencedRatio">Ratio of bytes that are allowed to not be referenced without
        /// compacting the file. If 100 bytes are in the structure and the ratio is .2, then the structure will be compacted to
        /// remove unreferenced items if more than 20 unique bytes are not retrieved during the lifetime of the object.</param>
        public FileCombiner(LoggingContext loggingContext, string path, FileCombinerUsage usage, bool logFileCombinerStatistics, double allowableUnreferencedRatio = .2)
            : this(loggingContext, path, usage, allowableUnreferencedRatio, DefaultMaxReadChunkBytes)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));
            m_logFileCombinerStatistics = logFileCombinerStatistics;
        }

        /// <summary>
        /// Constructor for unit testing
        /// </summary>
        internal FileCombiner(LoggingContext loggingContext, string path, FileCombinerUsage usage, double allowableUnreferencedRatio, int maxReadChunkBytes)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));
            Contract.Requires(maxReadChunkBytes > 0 && maxReadChunkBytes <= DefaultMaxReadChunkBytes);

            m_path = path;
            m_usage = usage;
            m_loggingContext = loggingContext;
            m_allowableUnreferencedRatio = allowableUnreferencedRatio;
            m_maxReadChunkBytes = maxReadChunkBytes;
            m_initializationTask = Task.Factory.StartNew(Initialize);
        }

        /// <summary>
        /// Gets a reference to the stats that will be populated at shutdown. Should only be used for testing
        /// </summary>
        internal void GetStatsRefForTest(ref Tracing.Logger.FileCombinerStats stats)
        {
            stats = m_stats;
        }

        /// <summary>
        /// Ensures the Initialize task has been completed. This is necessary before any interaction
        /// </summary>
        /// <returns>returns whether initialization completed successfully.
        /// If false, the backing structures may be invalid and should not be used.</returns>
        private bool EnsureInitializeCompleted()
        {
            var t = m_initializationTask;
            if (t != null)
            {
                lock (t)
                {
                    if (m_initializationTask != null)
                    {
                        m_initializationTask.Wait();
                        m_initializationTask = null;
                    }
                }
            }

            return m_enabled;
        }

        /// <summary>
        /// Initializes the structure by reading metadata and content from disk
        /// </summary>
        /// <remarks>
        /// The file format is as follows:
        ///
        ///  int version - version for the format of the file
        ///  long metadataOffset - offset in the file to where the file metadata starts
        ///  Clean/Dirty - describes whether the file was closed cleanly. Used as a hint to avoid work when
        ///     not closed cleanly
        ///  byte[] fileContent - actual content of files. This is at the start so additional file and metadata can be
        ///     written to the end without rewriting the content at the beginning of the file
        ///  FileLocation[] - metadata of which files exist, their hashes, and where they are
        /// </remarks>
        private void Initialize()
        {
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                if (!File.Exists(m_path))
                {
                    TryCreateClean();

                    // Don't go through a many layers of exception handling if the file doesn't even exist.
                    return;
                }

                m_file = FileUtilities.CreateFileStream(m_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
                m_reader = new BuildXLReader(false, m_file, leaveOpen: true);

                if (m_reader.ReadInt32() != Version)
                {
                    Tracing.Logger.Log.FileCombinerVersionIncremented(m_loggingContext, m_usage.ToString());
                    CreateClean();
                    return;
                }

                m_nextItemLocation = m_reader.ReadInt64();

                if (m_reader.ReadInt32() != Clean)
                {
                    Tracing.Logger.Log.FileCombinerFailedToInitialize(m_loggingContext, m_usage.ToString(), Strings.FileCombiner_FileNotClosedCleanly);
                    CreateClean();
                    return;
                }

                m_contentStartLocation = m_reader.BaseStream.Position;

                // Read out the file content in a single large read for best performance
                m_reader.BaseStream.Position = 0;
                Contract.Assume(m_maxReadChunkBytes > 0);
                int contentArrays = (int)((m_nextItemLocation + (m_maxReadChunkBytes - 1)) / m_maxReadChunkBytes);
                Contract.Assert(contentArrays > 0);
                m_content = new byte[contentArrays][];
                var bytesRemaining = m_nextItemLocation;
                for (int i = 0; i < m_content.Length; i++)
                {
                    var chunkSize = (int)Math.Min(bytesRemaining, m_maxReadChunkBytes);
                    m_content[i] = m_reader.ReadBytes(chunkSize);
                    bytesRemaining -= chunkSize;
                }

                Contract.Assert(bytesRemaining == 0);

                // read out the metadata
                Contract.Assert(m_reader.BaseStream.Position == m_nextItemLocation);

                int count = 0;

                // If the file combiner is empty, the end of the stream may have been reached.
                if (m_reader.BaseStream.Position < m_reader.BaseStream.Length)
                {
                    count = m_reader.ReadInt32Compact();
                }

                Contract.Assume(count >= 0);
                m_fileLocations = new FileLocation[count];
                long lastOffset = 0;
                for (int i = 0; i < count; i++)
                {
                    FileLocation fileLocation = FileLocation.Deserialize(m_reader);
                    if (fileLocation.Offset < lastOffset)
                    {
                        throw new BuildXLException("File location offsets should be increasing");
                    }

                    lastOffset = fileLocation.Offset;

                    m_fileLocations[i] = fileLocation;

                    // If the same path is encountered more than once, always overwrite with the most recently added one
                    m_filesByPath.AddOrUpdate(fileLocation.Path, i, (oldPath, oldInt) => i);
                }

                // Initialize the writer and return success
                m_writer = new BuildXLWriter(false, m_file, true, false);
                m_stats.BeginCount = count;
                m_stats.InitializationTimeMs = (int)sw.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                if (File.Exists(m_path))
                {
                    // Only log the warning if the file previously existed. Otherwise we just failed to open it which
                    // isn't an error case
#pragma warning disable EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                    Tracing.Logger.Log.FileCombinerFailedToInitialize(m_loggingContext, m_usage.ToString(), ex.Message);
#pragma warning restore EPC12 // Suspicious exception handling: only Message property is observed in exception block.
                }

                TryCreateClean();
            }
        }

        private void TryCreateClean()
        {
            try
            {
                CreateClean();
            }
            catch (Exception creationException)
            {
                // If creating a new FileCombiner fails, we should just disable it and no-op on accesses.
                Tracing.Logger.Log.FileCombinerFailedToCreate(m_loggingContext, m_usage.ToString(), creationException.GetLogEventMessage());
                m_enabled = false;
            }
        }

        /// <summary>
        /// Creates a clean backing file and state for the FileCombiner
        /// </summary>
        private void CreateClean()
        {
            if (m_file == null)
            {
                FileUtilities.CreateDirectory(Path.GetDirectoryName(m_path));
                m_file = new FileStream(m_path, FileMode.Create, FileAccess.ReadWrite, FileShare.Delete);
            }

            if (m_reader == null)
            {
                m_reader = new BuildXLReader(debug: false, stream: m_file, leaveOpen: true);
            }

            if (m_writer == null)
            {
                m_writer = new BuildXLWriter(debug: false, stream: m_file, leaveOpen: true, logStats: false);
            }

            m_fileLocations = CollectionUtilities.EmptyArray<FileLocation>();

            WritePreamble(clean: false, resetPosition: false);

            m_nextItemLocation = m_file.Position;
            m_contentStartLocation = m_reader.BaseStream.Position;
        }

        /// <summary>
        /// Writes the metadata at the very beginning of the file. Includes whether the file was cleanly closed or not
        /// </summary>
        /// <param name="clean">true if the file should be marked as being cleanly closed</param>
        /// <param name="resetPosition">true if the file position should be reset to its value before the call</param>
        private void WritePreamble(bool clean, bool resetPosition)
        {
            long originalPosition = m_file.Position;
            m_file.Position = 0;
            m_writer.Write(Version);
            m_writer.Write(m_nextItemLocation);

            // Clean/Dirty marker should be the last thing we write.
            m_writer.Write(clean ? Clean : Dirty);

            // Always flush to disk here since the Dirty/Clean state of the file is up to date
            m_writer.Flush();
            if (resetPosition)
            {
                m_file.Position = originalPosition;
            }
        }

        /// <summary>
        /// Marks the backing file as dirty.
        /// </summary>
        private void MarkDirty()
        {
            Contract.Requires(Monitor.IsEntered(m_writer));
            WritePreamble(clean: false, resetPosition: true);
        }

        /// <summary>
        /// Marks the backing file as clean.
        /// </summary>
        private void MarkClean()
        {
            Contract.Requires(Monitor.IsEntered(m_writer));
            WritePreamble(clean: true, resetPosition: true);
        }

        private bool ShouldCompact()
        {
            // Don't compact if there were no accesses. This will be the case in a graph cache hit
            if (m_hits + m_misses == 0)
            {
                return false;
            }

            double referencedBytes = m_fileLocations.Where(l => l.WasReferenced).Sum(l => (double)l.Length);
            double referencedRatio = referencedBytes / m_nextItemLocation;
            double unreferencedRatio = 1 - referencedRatio;

            Contract.Assume(unreferencedRatio >= 0);
            Contract.Assume(unreferencedRatio <= 1);
            int unreferencedPercent = (int)Math.Round(unreferencedRatio * 100, 0);
            m_stats.UnreferencedPercent = unreferencedPercent;

            return unreferencedRatio > m_allowableUnreferencedRatio;
        }

        /// <summary>
        /// Flushes content to disk
        /// </summary>
        private void CompleteWritingFile()
        {
            if (EnsureInitializeCompleted())
            {
                m_stats.Hits = m_hits;
                m_stats.Misses = m_misses;

                lock (m_writer)
                {
                    if (m_addedFileLocations.Count > 0)
                    {
                        if (ShouldCompact())
                        {
                            CompactFile();
                        }
                        else
                        {
                            // write out the metadata
                            m_writer.BaseStream.Position = m_nextItemLocation;
                            int count = m_fileLocations.Length + m_addedFileLocations.Count;
                            m_writer.WriteCompact(count);
                            m_stats.EndCount = count;
                            foreach (FileLocation fileLocation in m_fileLocations)
                            {
                                fileLocation.Serialize(m_writer);
                            }

                            foreach (FileLocation fileLocation in m_addedFileLocations)
                            {
                                fileLocation.Serialize(m_writer);
                            }
                        }
                    }

                    m_writer.Flush();

                    MarkClean();
                }
            }
        }

        /// <summary>
        /// Compacts the large file by removing entries that were not queried
        /// </summary>
        private void CompactFile()
        {
            Contract.Assert(Monitor.IsEntered(m_writer));

            Stopwatch sw = Stopwatch.StartNew();

            // Seek to the beginning
            m_reader.BaseStream.Position = m_contentStartLocation;
            m_nextItemLocation = m_contentStartLocation;

            // Compact the file content
            int contentCount = 0;
            for (int i = 0; i < m_fileLocations.Length; i++)
            {
                m_fileLocations[i] = MoveFileContent(m_fileLocations[i], ref contentCount);
            }

            for (int i = 0; i < m_addedFileLocations.Count; i++)
            {
                m_addedFileLocations[i] = MoveFileContent(m_addedFileLocations[i], ref contentCount);
            }

            // Write the compacted metadata
            m_writer.BaseStream.Position = m_nextItemLocation;
            m_writer.WriteCompact(contentCount);

            int metadataCount = 0;
            foreach (FileLocation fileLocation in m_fileLocations)
            {
                if (fileLocation.WasReferenced)
                {
                    fileLocation.Serialize(m_writer);
                    metadataCount++;
                }
            }

            foreach (FileLocation fileLocation in m_addedFileLocations)
            {
                if (fileLocation.WasReferenced)
                {
                    fileLocation.Serialize(m_writer);
                    metadataCount++;
                }
            }

            Contract.Assert(contentCount == metadataCount);

            // shrink the size of the file
            m_writer.BaseStream.SetLength(m_writer.BaseStream.Position);

            m_stats.FinalSizeInMB = (int)(m_writer.BaseStream.Position / (1024 * 1024));

            // Make sure the CompactingTime is always nonzero if compaction happened in case its less than 1ms
            m_stats.CompactingTimeMs = Math.Max(1, (int)sw.ElapsedMilliseconds);
            m_stats.EndCount = contentCount;
        }

        /// <summary>
        /// Moves a file's content to the next available spot
        /// </summary>
        private FileLocation MoveFileContent(FileLocation fileLocation, ref int referencedCount)
        {
            Contract.Assume(m_nextItemLocation <= fileLocation.Offset, "File content may only be moved to an earlier location");

            if (fileLocation.WasReferenced)
            {
                if (m_nextItemLocation != fileLocation.Offset)
                {
                    // Read the content from its original location
                    m_reader.BaseStream.Seek(fileLocation.Offset, SeekOrigin.Begin);
                    byte[] bytes = m_reader.ReadBytes(fileLocation.Length);

                    // Write it to the earlier location
                    fileLocation.Offset = m_nextItemLocation;
                    m_writer.BaseStream.Seek(m_nextItemLocation, SeekOrigin.Begin);
                    m_writer.Write(bytes);
                }

                m_nextItemLocation += fileLocation.Length;

                referencedCount++;
            }

            return fileLocation;
        }

        /// <summary>
        /// Adds a file
        /// </summary>
        public void AddFile(byte[] content, ContentHash hash, string path, int? contentLength = null)
        {
            AddFileCore(
                state: (content, contentLength ?? content.Length),
                hash: hash,
                path: path,
                contentLength: contentLength ?? content.Length,
                writeFunction: (writer, ctent) => writer.Write(ctent.content, 0, ctent.Item2));
        }

        /// <summary>
        /// Adds a file
        /// </summary>
        public void AddFile(Stream content, ContentHash hash, string path)
        {
            AddFileCore(
                state: this,
                hash: hash,
                path: path,
                contentLength: (int)content.Length,
                writeFunction: (writer, @this) =>
                {
                    @this.WriteStream(content);
                });
        }

        /// <summary>
        /// Helper function that copies content of the stream to the writer using a given buffer.
        /// </summary>
        private void WriteStream(Stream source)
        {
            int read;
            while ((read = source.Read(m_copyBuffer, 0, m_copyBuffer.Length)) != 0)
            {
                m_writer.Write(m_copyBuffer, 0, read);
            }
        }

        private void AddFileCore<T>(T state, ContentHash hash, string path, int contentLength, Action<BuildXLWriter, T> writeFunction)
        {
            if (EnsureInitializeCompleted())
            {
                lock (m_writer)
                {
                    FileLocation file = default(FileLocation);
                    file.Path = path;
                    file.Hash = hash;
                    file.Length = contentLength;
                    file.WasReferenced = true;
                    file.Offset = m_nextItemLocation;

                    // The first added file needs to flip the state of the backing file to dirty since each additional file
                    // overwrites the metadata of all existing files. The state will not be clean again until CompleteWritingFile()
                    if (m_addedFileLocations.Count == 0)
                    {
                        MarkDirty();
                    }

                    m_writer.BaseStream.Position = m_nextItemLocation;

                    writeFunction(m_writer, state);

                    m_addedFileLocations.Add(file);
                    m_nextItemLocation = m_writer.BaseStream.Position;
                    m_filesByPath.AddOrUpdate(path, UnavailableFileMarker, (existingPath, existingIndex) => UnavailableFileMarker);
                }
            }
        }

        /// <summary>
        /// Adds a file to the file combiner if that file does not already exist
        /// </summary>
        /// <remarks>
        /// Returns a stream corresponding to the stored content if the file was there, or the new content if the file was not stored
        /// </remarks>
        public MemoryStream GetOrAddFile(byte[] content, ContentHash hash, string path, int? contentLength = null)
        {
            var stream = RequestFile(path, hash);

            // Found the stream, don't add it and return
            if (stream != null)
            {
                return stream;
            }

            AddFile(content, hash, path, contentLength);
            return new MemoryStream(content, 0, contentLength ?? content.Length);
        }

        /// <summary>
        /// Requests an item by path and optionally matching a specific hash
        /// </summary>
        /// <param name="path">Absolute path of file to request</param>
        /// <param name="hash">Hash of file. If not set, the hash will not be checked</param>
        public MemoryStream RequestFile(string path, ContentHash? hash)
        {
            if (EnsureInitializeCompleted())
            {
                int position;
                bool found = m_filesByPath.TryGetValue(path, out position);

                if (found && position < m_fileLocations.Length && position != UnavailableFileMarker)
                {
                    m_fileLocations[position].WasReferenced = true;
                    FileLocation location = m_fileLocations[position];

                    if (hash.HasValue)
                    {
                        if (location.Hash != hash.Value)
                        {
                            Interlocked.Increment(ref m_misses);
                            return null;
                        }
                    }

                    Interlocked.Increment(ref m_hits);

                    int startArray = (int)(location.Offset / m_maxReadChunkBytes);
                    int startIndex = (int)(location.Offset % m_maxReadChunkBytes);
                    if (location.Offset + location.Length <= ((long)startArray * m_maxReadChunkBytes) + m_maxReadChunkBytes)
                    {
                        return new MemoryStream(buffer: m_content[startArray], index: startIndex, count: location.Length, writable: false, publiclyVisible: false);
                    }
                    else
                    {
                        byte[] content = new byte[location.Length];
                        var contentOffset = 0;
                        while (contentOffset < content.Length)
                        {
                            var chunkSize = Math.Min(m_maxReadChunkBytes - startIndex, content.Length - contentOffset);
                            Contract.Assert(chunkSize > 0);
                            Array.Copy(m_content[startArray], startIndex, content, contentOffset, chunkSize);
                            startArray++;
                            startIndex = 0;
                            contentOffset += chunkSize;
                        }

                        return new MemoryStream(content);
                    }
                }
            }

            Interlocked.Increment(ref m_misses);
            return null;
        }

        /// <summary>
        /// Disposes
        /// </summary>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                Dispose(true);
            }
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Make sure the initialization task has finished
                EnsureInitializeCompleted();
                Contract.Assert(m_initializationTask == null, "EnsureInitializeCompleted sets m_initializationTask to null");

                CompleteWritingFile();

                if (m_logFileCombinerStatistics)
                {
                    switch (m_usage)
                    {
                        case FileCombinerUsage.SpecFileCache:
                            Tracing.Logger.Log.SpecCache(m_loggingContext, m_stats);
                            break;
                        default:
                            Contract.Assume(m_usage == FileCombinerUsage.IncrementalScriptFrontEnd, "Unexpected FileCombinerUsage");
                            Tracing.Logger.Log.IncrementalFrontendCache(m_loggingContext, m_stats);
                            break;
                    }
                }

                m_reader?.Dispose();
                m_writer?.Dispose();
                m_file?.Dispose();

                m_content = null;
            }
        }
    }
}
