// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Processes.Sideband
{
    /// <summary>
    /// Responsible for keeping a log of all paths written to by a given process pip.
    /// 
    /// Paths are flushed to an underlying file as soon as they're reported via 
    /// <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>.
    /// 
    /// A number of root directories may be set in which case the log will record only 
    /// those paths that fall under one of those directories.  The typical use case is to
    /// use all shared opaque directory outputs of a process as root directories.
    /// </summary>
    /// <remarks>
    /// NOTE: not thread-safe
    /// 
    /// NOTE: must be serializable in order to be compatible with VM execution; for this 
    ///       reason, this class must not have a field of type <see cref="PathTable"/>.
    /// </remarks>
    public sealed class SidebandWriter : IDisposable
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "SharedOpaqueSidebandState", version: 0);

        private readonly HashSet<AbsolutePath> m_recordedPathsCache;
        private readonly Lazy<BuildXLWriter> m_lazyBxlWriter;
        private readonly FileEnvelopeId m_envelopeId;

        private IReadOnlyList<AbsolutePath> m_convertedRootDirectories = null;

        private IReadOnlyList<AbsolutePath> GetConvertedRootDirectories(PathTable pathTable)
        {
            if (m_convertedRootDirectories == null)
            {
                lock (m_lazyBxlWriter)
                {
                    if (m_convertedRootDirectories == null)
                    {
                        m_convertedRootDirectories = RootDirectories.Select(dir => AbsolutePath.Create(pathTable, dir)).ToList();
                    }
                }
            }

            return m_convertedRootDirectories;
        }

        /// <summary>
        /// Absolute path of the sideband file this logger writes to.
        /// </summary>
        public string SidebandLogFile { get; }

        /// <summary>
        /// Associated metadata
        /// </summary>
        public SidebandMetadata Metadata { get; }

        /// <summary>
        /// Only paths under these root directories will be recorded by <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>
        /// 
        /// When <code>null</code>, all paths are recorded.
        /// </summary>
        [CanBeNull]
        public IReadOnlyList<string> RootDirectories { get; }

        /// <summary>
        /// Creates a new output logger for a given process.
        /// 
        /// Shared opaque directory outputs of <paramref name="process"/> are used as root directories and
        /// <see cref="GetSidebandFileForProcess"/> is used as log base name.
        ///
        /// <seealso cref="SidebandWriter(SidebandMetadata, string, IReadOnlyList{string})"/>
        /// </summary>
        public SidebandWriter(SidebandMetadata metadata, PipExecutionContext context, Process process, AbsolutePath sidebandRootDirectory)
            : this(
                  metadata,
                  GetSidebandFileForProcess(context.PathTable, sidebandRootDirectory, process),
                  process.DirectoryOutputs.Where(d => d.IsSharedOpaque).Select(d => d.Path.ToString(context.PathTable)).ToList())
        {
            Contract.Requires(process != null);
            Contract.Requires(context != null);
            Contract.Requires(sidebandRootDirectory.IsValid);
            Contract.Requires(Directory.Exists(sidebandRootDirectory.ToString(context.PathTable)));
        }

        /// <summary>
        /// Creates a new output logger.
        /// 
        /// The underlying file is created only upon first write.
        /// </summary>
        /// <param name="metadata">Metadata</param>
        /// <param name="sidebandLogFile">File to which to save the log.</param>
        /// <param name="rootDirectories">Only paths under one of the root directories are recorded in <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>.</param>
        public SidebandWriter(SidebandMetadata metadata, string sidebandLogFile, [CanBeNull] IReadOnlyList<string> rootDirectories)
        {
            Metadata = metadata;
            SidebandLogFile = sidebandLogFile;
            RootDirectories = rootDirectories;
            m_recordedPathsCache = new HashSet<AbsolutePath>();
            m_envelopeId = FileEnvelopeId.Create();

            m_lazyBxlWriter = Lazy.Create(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SidebandLogFile));
                var writer = new BuildXLWriter(
                    stream: new FileStream(SidebandLogFile, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete),
                    debug: false,
                    logStats: false,
                    leaveOpen: false);

                // write header and metadata before anything else
                FileEnvelope.WriteHeader(writer.BaseStream, m_envelopeId);
                Metadata.Serialize(writer);
                return writer;
            });
        }

        private const string SidebandFilePrefix = "Pip";
        private const string SidebandFileSuffix = ".sideband";

        /// <summary>
        /// Given a root directory (<paramref name="searchRootDirectory"/>), returns the full path to the sideband file corresponding to process <paramref name="process"/>.
        /// </summary>
        public static string GetSidebandFileForProcess(PathTable pathTable, AbsolutePath searchRootDirectory, Process process)
        {
            Contract.Requires(searchRootDirectory.IsValid);

            var semiStableHashX16 = string.Format(CultureInfo.InvariantCulture, "{0:X16}", process.SemiStableHash);
            var subDirName = semiStableHashX16.Substring(0, 3);

            return searchRootDirectory.Combine(pathTable, subDirName).Combine(pathTable, $"{SidebandFilePrefix}{semiStableHashX16}{SidebandFileSuffix}").ToString(pathTable);
        }

        /// <summary>
        /// Finds and returns all sideband files that exist in directory denoted by <paramref name="directory"/>
        /// </summary>
        /// <remarks>
        /// CODESYNC: must be consistent with <see cref="GetSidebandFileForProcess(PathTable, AbsolutePath, Process)"/>
        /// </remarks>
        public static string[] FindAllProcessPipSidebandFiles(string directory)
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, $"{SidebandFilePrefix}*{SidebandFileSuffix}", SearchOption.AllDirectories).ToArray()
                : CollectionUtilities.EmptyArray<string>();
        }

        /// <summary>
        /// Returns all paths recorded in the <paramref name="filePath"/> file, even if the
        /// file appears to be corrupted.
        /// 
        /// If file at <paramref name="filePath"/> does not exist, returns an empty iterator.
        /// 
        /// <seealso cref="SidebandReader.ReadRecordedPaths"/>
        /// </summary>
        public static string[] ReadRecordedPathsFromSidebandFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return CollectionUtilities.EmptyArray<string>();
            }

            using (var reader = new SidebandReader(filePath))
            {
                reader.ReadHeader(ignoreChecksum: true);
                reader.ReadMetadata();
                return reader.ReadRecordedPaths().ToArray();
            }
        }

        /// <summary>
        /// By calling this method a client can ensure that the sideband file will be created even
        /// if 0 paths are recorded for it.  If this method is not explicitly called, the sideband
        /// file will only be created if at least one write is recorded to it.
        /// </summary>
        public void EnsureHeaderWritten()
        {
            Analysis.IgnoreResult(m_lazyBxlWriter.Value);
        }

        /// <summary>
        /// <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>
        /// </summary>
        public bool RecordFileWrite(PathTable pathTable, string absolutePath)
            => RecordFileWrite(pathTable, AbsolutePath.Create(pathTable, absolutePath));

        /// <summary>
        /// Records that the file at location <paramref name="path"/> was written to.
        /// </summary>
        /// <returns>
        /// <code>true</code> if <paramref name="path"/> is within any given root directory
        /// (<see cref="RootDirectories"/>) and was recorded this time around (i.e., wasn't
        /// a duplicate of a previously recorded path); <code>false</code> otherwise.
        /// </returns>
        /// <remarks>
        /// NOT THREAD-SAFE.
        /// </remarks>
        public bool RecordFileWrite(PathTable pathTable, AbsolutePath path)
        {
            if (RootDirectories == null || GetConvertedRootDirectories(pathTable).Any(dir => path.IsWithin(pathTable, dir)))
            {
                if (m_recordedPathsCache.Add(path))
                {
                    m_lazyBxlWriter.Value.Write(path.ToString(pathTable));
                    m_lazyBxlWriter.Value.Flush();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Don't use, for testing only.
        /// </summary>
        internal void CloseWriterWithoutFixingUpHeaderForTestingOnly()
        {
            m_lazyBxlWriter.Value.Close();
        }

        /// <nodoc />
        public void Dispose()
        {
            // NOTE: it is essential not to call m_lazyBxlWriter.Value.Dispose() if bxlWriter hasn't been created.
            // 
            // reason: 
            //   - when running a process in VM, a logger is created twice for that process: (1) first in the
            //     bxl process, and (2) second in the VM process
            //   - the VM process then runs, and writes stuff to its instance of this logger; once it finishes, 
            //     all shared opaque output writes are saved to the underlying sideband file
            //   - the bxl process disposes its instance of this logger; without the check below, the Dispose method
            //     creates a BuildXLWriter for the same underlying sideband file and immediately closes it, which
            //     effectively deletes the content of that file.
            if (m_lazyBxlWriter.IsValueCreated)
            {
                FileEnvelope.FixUpHeader(m_lazyBxlWriter.Value.BaseStream, m_envelopeId);
                m_lazyBxlWriter.Value.Dispose();
            }
        }

        #region Serialization
        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            Metadata.Serialize(writer);
            writer.Write(SidebandLogFile);
            writer.Write(RootDirectories, (w, list) => w.WriteReadOnlyList(list, (w2, path) => w2.Write(path)));
        }

        /// <nodoc />
        public static SidebandWriter Deserialize(BuildXLReader reader)
        {
            return new SidebandWriter(
                metadata: SidebandMetadata.Deserialize(reader),
                sidebandLogFile: reader.ReadString(),
                rootDirectories: reader.ReadNullable(r => r.ReadReadOnlyList(r2 => r2.ReadString())));
        }
        #endregion
    }
}
