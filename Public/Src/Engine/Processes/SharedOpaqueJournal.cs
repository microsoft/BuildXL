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

namespace BuildXL.Processes
{
    /// <summary>
    /// Responsible for keeping a journal of all paths written to by a given process pip.
    /// 
    /// Paths are flushed to an underlying file as soon as they're reported via <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>.
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
        private readonly HashSet<AbsolutePath> m_recordedPathsCache;
        private readonly Lazy<BuildXLWriter> m_lazyBxlWriter;

        /// <summary>
        /// Absolute path of this journal file.
        /// </summary>
        public string JournalPath { get; }

        /// <summary>
        /// Only paths under these root directories will be recorded by <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>
        /// 
        /// When <code>null</code>, all paths are recorded.
        /// </summary>
        [CanBeNull]
        public IReadOnlyList<string> RootDirectories { get; }

        /// <summary>
        /// Creates a journal for a given process.
        /// 
        /// Shared opaque directory outputs of <paramref name="process"/> are used as root directories and
        /// <see cref="Pip.FormattedSemiStableHash"/> is used as journal base name.
        ///
        /// <seealso cref="SharedOpaqueJournal(string, IReadOnlyList{string})"/>
        /// </summary>
        public SharedOpaqueJournal(PipExecutionContext context, Process process, AbsolutePath journalDirectory)
            : this(
                  GetJournalFileForProcess(context.PathTable, journalDirectory, process),
                  process.DirectoryOutputs.Where(d => d.IsSharedOpaque).Select(d => d.Path.ToString(context.PathTable)).ToList())
        {
            Contract.Requires(process != null);
            Contract.Requires(context != null);
            Contract.Requires(journalDirectory.IsValid);
            Contract.Requires(Directory.Exists(journalDirectory.ToString(context.PathTable)));
        }

        /// <summary>
        /// Creates a new journal.
        /// </summary>
        /// <param name="journalPath">File to which to write recorded accesses.</param>
        /// <param name="rootDirectories">Only paths under one of the root directories are recorded in <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>.</param>
        public SharedOpaqueJournal(string journalPath, [CanBeNull] IReadOnlyList<string> rootDirectories)
        {
            JournalPath = journalPath;
            RootDirectories = rootDirectories;
            m_recordedPathsCache = new HashSet<AbsolutePath>();

            m_lazyBxlWriter = Lazy.Create(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(JournalPath));
                return new BuildXLWriter(
                    stream: new FileStream(JournalPath, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete),
                    debug: false,
                    logStats: false,
                    leaveOpen: false);
            });
        }

        /// <summary>
        /// Given a root directory (<paramref name="journalDirectory"/>), returns the full path to the journal corresponding to process <paramref name="process"/>.
        /// </summary>
        public static string GetJournalFileForProcess(PathTable pathTable, AbsolutePath journalDirectory, Process process)
        {
            Contract.Requires(journalDirectory.IsValid);

            var semiStableHashX16 = string.Format(CultureInfo.InvariantCulture, "{0:X16}", process.SemiStableHash);
            var subDirName = semiStableHashX16.Substring(0, 3);

            return journalDirectory.Combine(pathTable, subDirName).Combine(pathTable, $"Pip{semiStableHashX16}.journal").ToString(pathTable);
        }

        /// <summary>
        /// Finds and returns all journal files that exist in directory denoted by <paramref name="directory"/>
        /// </summary>
        /// <remarks>
        /// CODESYNC: must be consistent with <see cref="GetJournalFileForProcess(PathTable, AbsolutePath, Process)"/>
        /// </remarks>
        public static string[] FindAllProcessPipJournalFiles(string directory)
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "Pip*.journal", SearchOption.AllDirectories).ToArray()
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
        /// <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>
        /// </summary>
        public bool RecordFileWrite(PathTable pathTable, string absolutePath)
            => RecordFileWrite(pathTable, AbsolutePath.Create(pathTable, absolutePath));

        /// <summary>
        /// Records that the file at location <paramref name="path"/> was written to.
        /// 
        /// Returns whether the path was recorded or skipped.
        /// </summary>
        /// <returns>
        /// <code>true</code> if <paramref name="path"/> was recorded, <code>false</code> 
        /// if the path was filtered out because of <see cref="RootDirectories"/>.
        /// </returns>
        /// <remarks>
        /// NOT THREAD-SAFE.
        /// </remarks>
        public bool RecordFileWrite(PathTable pathTable, AbsolutePath path)
        {
            if (RootDirectories == null || RootDirectories.Any(dir => path.IsWithin(pathTable, AbsolutePath.Create(pathTable, dir))))
            {
                if (m_recordedPathsCache.Add(path))
                {
                    m_lazyBxlWriter.Value.Write(path.ToString(pathTable));
                    m_lazyBxlWriter.Value.Flush();
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
            m_lazyBxlWriter.Value.Dispose();
        }

        #region Serialization
        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(JournalPath);
            writer.Write(RootDirectories, (w, list) => w.WriteReadOnlyList(list, (w2, path) => w2.Write(path)));
        }

        /// <nodoc />
        public static SharedOpaqueJournal Deserialize(BuildXLReader reader)
        {
            return new SharedOpaqueJournal(
                journalPath: reader.ReadString(),
                rootDirectories: reader.ReadNullable(r => r.ReadReadOnlyList(r2 => r2.ReadString())));
        }
        #endregion

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