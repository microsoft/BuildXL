// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Processes
{
    /// <summary>
    /// Responsible for keeping a journal of all paths written to by a given process pip.
    /// 
    /// Paths are flushed to an underlying file as soon as they're reported via <see cref="RecordFileWrite(AbsolutePath)"/>.
    /// 
    /// A number of root directories may be set in which case the journal will record only 
    /// those paths that fall under one of those directories.  The typical use case is to
    /// use all shared opaque directory outputs of a process as root directories.
    /// </summary>
    /// <remarks>
    /// NOTE: not thread-safe
    /// </remarks>
    public sealed class SharedOpaqueJournal : IDisposable
    {
        [CanBeNull]
        private readonly IReadOnlyCollection<AbsolutePath> m_rootDirectories;
        private readonly PathTable m_pathTable;
        private readonly BuildXLWriter m_bxlWriter;
        private readonly HashSet<AbsolutePath> m_recordedPathsCache;

        /// <summary>
        /// Absolute path of this journal file.
        /// </summary>
        public string JournalPath { get; }

        /// <summary>
        /// Absolute path of the directory where this journal file is saved.
        /// </summary>
        public string JournalDirectory => Path.GetDirectoryName(JournalPath);

        /// <summary>
        /// Creates a journal for a given process.
        /// 
        /// Shared opaque directory outputs of <paramref name="process"/> are used as root directories and
        /// <see cref="Pip.FormattedSemiStableHash"/> is used as journal base name.
        ///
        /// <seealso cref="SharedOpaqueJournal(PathTable, AbsolutePath, IReadOnlyCollection{AbsolutePath})"/>
        /// </summary>
        public SharedOpaqueJournal(PipExecutionContext context, Process process, AbsolutePath journalDirectory)
            : this(
                  context.PathTable, 
                  GetJournalFileForProcess(context.PathTable, journalDirectory, process),
                  process.DirectoryOutputs.Where(d => d.IsSharedOpaque).Select(d => d.Path).ToList())
        {
            Contract.Requires(process != null);
            Contract.Requires(context != null);
            Contract.Requires(journalDirectory.IsValid);
            Contract.Requires(Directory.Exists(journalDirectory.ToString(context.PathTable)));
        }

        /// <summary>
        /// Creates a new journal.
        /// </summary>
        /// <param name="pathTable">Path table.</param>
        /// <param name="journalPath">File to which to write recorded accesses.</param>
        /// <param name="rootDirectories">Only paths under one of the root directories are recorded in <see cref="RecordFileWrite(AbsolutePath)"/>.</param>
        public SharedOpaqueJournal(PathTable pathTable, AbsolutePath journalPath, [CanBeNull] IReadOnlyCollection<AbsolutePath> rootDirectories)
        {
            Contract.Requires(pathTable.IsValid);

            m_pathTable = pathTable;
            m_rootDirectories = rootDirectories;
            m_recordedPathsCache = new HashSet<AbsolutePath>();

            JournalPath = journalPath.ToString(pathTable);
            m_bxlWriter = new BuildXLWriter(
                stream: new FileStream(JournalPath, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete),
                debug: false,
                logStats: false,
                leaveOpen: false);
        }

        /// <summary>
        /// Given a root directory (<paramref name="journalDirectory"/>), returns the full path to the journal corresponding to process <paramref name="process"/>.
        /// </summary>
        public static AbsolutePath GetJournalFileForProcess(PathTable pathTable, AbsolutePath journalDirectory, Process process)
        {
            Contract.Requires(journalDirectory.IsValid);
            return journalDirectory.Combine(pathTable, process.FormattedSemiStableHash);
        }

        /// <summary>
        /// Returns all paths recorded in the journal file <paramref name="journalFile"/>.
        /// </summary>
        /// <remarks>
        /// Those paths are expected to be absolute paths of files/directories that were written to by the previous build.
        ///
        /// NOTE: this method does not validate the recorded paths in any way.  That means that each returned string may be
        ///   - an invalid path
        ///   - a path pointing to an absent file
        ///   - a path pointing to a file
        ///   - a path pointing to a directory.
        ///
        /// NOTE: the strings in the returned enumerable are neither sorted nor deduplicated.
        /// </remarks>
        public static IEnumerable<string> ReadRecordedWritesFromJournal(string journalFile)
        {
            using (var fileStream = new FileStream(journalFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var bxlReader = new BuildXLReader(stream: fileStream, debug: false, leaveOpen: false))
            {
                string nextString = null;
                while ((nextString = ReadStringOrNull(bxlReader)) != null)
                {
                    yield return nextString;
                }
            }
        }

        /// <summary>
        /// Same as <see cref="ReadRecordedWritesFromJournal(string)"/> except that all exceptions are wrapped in <see cref="BuildXLException"/>
        /// </summary>
        public static string[] ReadRecordedWritesFromJournalWrapExceptions(string journalFile)
        {
            try
            {
                return ReadRecordedWritesFromJournal(journalFile).ToArray();
            }
            catch (Exception e)
            {
                throw new BuildXLException("Failed to read from shared opaque journal", e);
            }
        }

        /// <summary>
        /// Records that the file at location <paramref name="path"/> was written to.
        /// 
        /// Returns whether the path was recorded or skipped.
        /// </summary>
        /// <returns>
        /// <code>true</code> if <paramref name="path"/> was recorded, <code>false</code> 
        /// if the path was filtered out because of <see cref="m_rootDirectories"/>.
        /// </returns>
        /// <remarks>
        /// NOT THREAD-SAFE.
        /// </remarks>
        public bool RecordFileWrite(AbsolutePath path)
        {
            if (m_rootDirectories == null || m_rootDirectories.Any(dir => path.IsWithin(m_pathTable, dir)))
            {
                if (m_recordedPathsCache.Add(path))
                {
                    m_bxlWriter.Write(path.ToString(m_pathTable));
                    m_bxlWriter.Flush();
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <nodoc />
        public void Dispose()
        {
            m_bxlWriter?.Dispose();
        }

        private static string ReadStringOrNull(BuildXLReader bxlReader)
        {
            try
            {
                return bxlReader.ReadString();
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}