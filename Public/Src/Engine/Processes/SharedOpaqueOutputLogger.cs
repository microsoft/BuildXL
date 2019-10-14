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
    /// Responsible for keeping a log of all paths written to by a given process pip.
    /// 
    /// Paths are flushed to an underlying file as soon as they're reported via <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>.
    /// 
    /// A number of root directories may be set in which case the log will record only 
    /// those paths that fall under one of those directories.  The typical use case is to
    /// use all shared opaque directory outputs of a process as root directories.
    /// </summary>
    /// <remarks>
    /// NOTE: not thread-safe
    /// </remarks>
    public sealed class SharedOpaqueOutputLogger : IDisposable
    {
        private readonly HashSet<AbsolutePath> m_recordedPathsCache;
        private readonly Lazy<BuildXLWriter> m_lazyBxlWriter;

        /// <summary>
        /// Absolute path of this log file.
        /// </summary>
        public string LogPath { get; }

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
        /// <see cref="GetOutputLogFileForProcess"/> is used as log base name.
        ///
        /// <seealso cref="SharedOpaqueOutputLogger(string, IReadOnlyList{string})"/>
        /// </summary>
        public SharedOpaqueOutputLogger(PipExecutionContext context, Process process, AbsolutePath logDirectory)
            : this(
                  GetOutputLogFileForProcess(context.PathTable, logDirectory, process),
                  process.DirectoryOutputs.Where(d => d.IsSharedOpaque).Select(d => d.Path.ToString(context.PathTable)).ToList())
        {
            Contract.Requires(process != null);
            Contract.Requires(context != null);
            Contract.Requires(logDirectory.IsValid);
            Contract.Requires(Directory.Exists(logDirectory.ToString(context.PathTable)));
        }

        /// <summary>
        /// Creates a new output logger.
        /// 
        /// The underlying file is created only upon first write.
        /// </summary>
        /// <param name="logPath">File to which to save the log.</param>
        /// <param name="rootDirectories">Only paths under one of the root directories are recorded in <see cref="RecordFileWrite(PathTable, AbsolutePath)"/>.</param>
        public SharedOpaqueOutputLogger(string logPath, [CanBeNull] IReadOnlyList<string> rootDirectories)
        {
            LogPath = logPath;
            RootDirectories = rootDirectories;
            m_recordedPathsCache = new HashSet<AbsolutePath>();

            m_lazyBxlWriter = Lazy.Create(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                return new BuildXLWriter(
                    stream: new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete),
                    debug: false,
                    logStats: false,
                    leaveOpen: false);
            });
        }

        private const string LogFilePrefix = "Pip";
        private const string LogFileSuffix = ".outlog";

        /// <summary>
        /// Given a root directory (<paramref name="searchRootDirectory"/>), returns the full path to the log file corresponding to process <paramref name="process"/>.
        /// </summary>
        public static string GetOutputLogFileForProcess(PathTable pathTable, AbsolutePath searchRootDirectory, Process process)
        {
            Contract.Requires(searchRootDirectory.IsValid);

            var semiStableHashX16 = string.Format(CultureInfo.InvariantCulture, "{0:X16}", process.SemiStableHash);
            var subDirName = semiStableHashX16.Substring(0, 3);

            return searchRootDirectory.Combine(pathTable, subDirName).Combine(pathTable, $"{LogFilePrefix}{semiStableHashX16}{LogFileSuffix}").ToString(pathTable);
        }

        /// <summary>
        /// Finds and returns all output log files that exist in directory denoted by <paramref name="directory"/>
        /// </summary>
        /// <remarks>
        /// CODESYNC: must be consistent with <see cref="GetOutputLogFileForProcess(PathTable, AbsolutePath, Process)"/>
        /// </remarks>
        public static string[] FindAllProcessPipOutputLogFiles(string directory)
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, $"{LogFilePrefix}*{LogFileSuffix}", SearchOption.AllDirectories).ToArray()
                : CollectionUtilities.EmptyArray<string>();
        }

        /// <summary>
        /// Returns all paths recorded in the <paramref name="filePath"/> file.
        /// </summary>
        /// <remarks>
        /// Those paths are expected to be absolute paths of files/directories that were written to by the previous build.
        ///
        /// NOTE: this method does not validate the recorded paths in any way.  That means that each returned string may be
        ///   - a path pointing to an absent file
        ///   - a path pointing to a file
        ///   - a path pointing to a directory.
        /// 
        /// NOTE: if the log file was produced by an instance of this class (and wasn't corrupted in any way)
        ///   - the strings in the returned enumerable are all legal paths
        ///   - the returned collection does not contain any duplicates
        /// </remarks>
        public static IEnumerable<string> ReadRecordedPathsFromSharedOpaqueOutputLog(string filePath)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
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
        /// Same as <see cref="ReadRecordedPathsFromSharedOpaqueOutputLog(string)"/> except that all exceptions are wrapped in <see cref="BuildXLException"/>
        /// </summary>
        public static string[] ReadRecordedPathsFromSharedOpaqueOutputLogWrapExceptions(string filePath)
        {
            try
            {
                return ReadRecordedPathsFromSharedOpaqueOutputLog(filePath).ToArray();
            }
            catch (Exception e)
            {
                throw new BuildXLException("Failed to read from shared opaque output log", e);
            }
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
        /// <code>true</code> if <paramref name="path"/> is within any given root directory (<see cref="RootDirectories"/>) and hence was recorded; <code>false</code> otherwise.
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
            writer.Write(LogPath);
            writer.Write(RootDirectories, (w, list) => w.WriteReadOnlyList(list, (w2, path) => w2.Write(path)));
        }

        /// <nodoc />
        public static SharedOpaqueOutputLogger Deserialize(BuildXLReader reader)
        {
            return new SharedOpaqueOutputLogger(
                logPath: reader.ReadString(),
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
