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

namespace BuildXL.Processes
{
    /// <summary>
    /// TODO 
    /// </summary>
    /// <remarks>
    /// NOTE: not thread-safe
    /// </remarks>
    public sealed class SharedOpaqueJournal : IDisposable
    {
        private readonly PathTable m_pathTable;
        private readonly AbsolutePath[] m_rootDirectories;
        private readonly BuildXLWriter m_bxlWriter;

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
        /// <seealso cref="SharedOpaqueJournal(PathTable, IEnumerable{AbsolutePath}, AbsolutePath)"/>
        /// </summary>
        public SharedOpaqueJournal(PipExecutionContext context, Process process, AbsolutePath journalDirectory)
            : this(
                  context.PathTable, 
                  process.DirectoryOutputs.Where(d => d.IsSharedOpaque).Select(d => d.Path),
                  GetJournalFileForProcess(context.PathTable, journalDirectory, process))
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
        /// <param name="rootDirectories">Only paths under one of the root directories are recorded in <see cref="RecordFileWrite(AbsolutePath)"/>.</param>
        /// <param name="journalPath">File to which to write recorded accesses.</param>
        public SharedOpaqueJournal(PathTable pathTable, IEnumerable<AbsolutePath> rootDirectories, AbsolutePath journalPath)
        {
            Contract.Requires(pathTable.IsValid);
            Contract.Requires(rootDirectories != null);

            m_pathTable = pathTable;
            m_rootDirectories = rootDirectories.ToArray();

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
        /// Finds and returns all journal files that exist in directory denoted by <paramref name="directory"/>
        /// </summary>
        public static IEnumerable<string> FindAllJournalFiles(string directory)
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                : CollectionUtilities.EmptyArray<string>();
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
            bool isUnderSharedOpaqueDirectory = m_rootDirectories.Any(dir => path.IsWithin(m_pathTable, dir));
            if (!isUnderSharedOpaqueDirectory)
            {
                return false;
            }

            m_bxlWriter.Write(path.ToString(m_pathTable));
            m_bxlWriter.Flush();
            return true;
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