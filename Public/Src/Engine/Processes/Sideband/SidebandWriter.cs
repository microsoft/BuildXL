// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Core;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Processes.Sideband
{
    /// <summary>
    /// Responsible for keeping a log of all paths written to by a given process pip.
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
        /// Only paths under these root directories will be recorded by
        /// <see cref="RecordFileWrite(PathTable, AbsolutePath, bool)"/>
        /// and <see cref="RecordFileWrite(PathTable, string, bool)"/>.
        /// 
        /// When <code>null</code>, all paths are recorded.
        /// </summary>
        [MaybeNull]
        public IReadOnlyList<string> RootDirectories { get; }

        /// <summary>
        /// Creates a new output logger.
        /// 
        /// The underlying file is created only upon first write.
        /// </summary>
        /// <param name="metadata">Metadata</param>
        /// <param name="sidebandLogFile">File to which to save the log.</param>
        /// <param name="rootDirectories">Only paths under one of these root directories will be recorded.</param>
        public SidebandWriter(SidebandMetadata metadata, string sidebandLogFile, [MaybeNull] IReadOnlyList<string> rootDirectories)
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
        /// <see cref="RecordFileWrite(PathTable, AbsolutePath, bool)"/>
        /// </summary>
        public bool RecordFileWrite(PathTable pathTable, string absolutePath, bool flushImmediately)
            => RecordFileWrite(pathTable, AbsolutePath.Create(pathTable, absolutePath), flushImmediately);

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
        public bool RecordFileWrite(PathTable pathTable, AbsolutePath path, bool flushImmediately)
        {
            if (RootDirectories == null || path.IsWithin(pathTable, GetConvertedRootDirectories(pathTable)))
            {
                if (m_recordedPathsCache.Add(path))
                {
                    m_lazyBxlWriter.Value.Write(path.ToString(pathTable));
                    if (flushImmediately)
                    {
                        m_lazyBxlWriter.Value.Flush();
                    }
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
            //   - when running a process in VM, a sideband writer is created twice for that process: (1) first in the
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
