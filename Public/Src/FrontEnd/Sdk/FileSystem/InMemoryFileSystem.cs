// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Sdk.FileSystem
{
    /// <summary>
    /// An IFileSystem where content can be explicitly added by path where it is only present in memory.
    /// </summary>
    /// <remarks>
    /// When adding content (either files or directories), all parent directories
    /// up to the root are automatically added as well
    /// </remarks>
    public sealed class InMemoryFileSystem : IMutableFileSystem
    {
        private readonly HashSet<AbsolutePath> m_directories;
        private readonly PathTable m_pathTable;
        private readonly Dictionary<AbsolutePath, string> m_pathToContent;

        /// <inheritdoc />
        /// <nodoc />
        public InMemoryFileSystem(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);
            m_pathToContent = new Dictionary<AbsolutePath, string>();
            m_directories = new HashSet<AbsolutePath>();
            m_pathTable = pathTable;
        }

        /// <inheritdoc />
        IFileSystem IFileSystem.CopyWithNewPathTable(PathTable newPathTable)
        {
            var oldPathTable = m_pathTable;
            var result = new InMemoryFileSystem(newPathTable);

            foreach (var kv in m_pathToContent)
            {
                result.m_pathToContent.Add(AbsolutePath.Create(newPathTable, kv.Key.ToString(oldPathTable)), kv.Value);
            }

            foreach (var dir in m_directories)
            {
                result.m_directories.Add(AbsolutePath.Create(newPathTable, dir.ToString(oldPathTable)));
            }

            return result;
        }

        /// <nodoc />
        public IMutableFileSystem WriteAllText(string path, string content)
        {
            return WriteAllText(AbsolutePath.Create(m_pathTable, path), content);
        }

        /// <nodoc />
        public IMutableFileSystem WriteAllText(AbsolutePath path, string content)
        {
            m_pathToContent[path] = content;
            AddAllUpstreamDirectories(path.GetParent(m_pathTable));
            return this;
        }

        /// <nodoc />
        public IMutableFileSystem CreateDirectory(AbsolutePath path)
        {
            AddAllUpstreamDirectories(path);
            return this;
        }

        /// <nodoc />
        public StreamReader OpenText(AbsolutePath path)
        {
            Contract.Requires(Exists(path));
            var content = m_pathToContent[path];

            var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            // StreamReader closes an underlying stream by default.
            return new StreamReader(memoryStream);
        }

        /// <nodoc />
        public bool Exists(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return m_pathToContent.ContainsKey(path) || m_directories.Contains(path);
        }

        /// <nodoc />
        public bool IsDirectory(AbsolutePath path)
        {
            return m_directories.Contains(path);
        }

        /// <nodoc />
        public string GetBaseName(AbsolutePath path)
        {
            return path.GetName(m_pathTable).ToString(m_pathTable.StringTable);
        }

        /// <inheritdoc />
        public PathTable GetPathTable()
        {
            return m_pathTable;
        }

        /// <inheritdoc />
        public IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, string pattern = " * ", bool recursive = false)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(IsDirectory(path));
            Contract.Requires(!string.IsNullOrEmpty(pattern));
            return FindMatches(m_directories, path, pattern, recursive);
        }

        /// <inheritdoc />
        public IEnumerable<AbsolutePath> EnumerateFiles(AbsolutePath path, string pattern = "*", bool recursive = false)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(IsDirectory(path));
            Contract.Requires(!string.IsNullOrEmpty(pattern));
            return FindMatches(m_pathToContent.Keys, path, pattern, recursive);
        }

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool enumerateDirectory,
            string pattern,
            uint directoriesToSkipRecursively,
            bool recursive,
            IDirectoryEntriesAccumulator accumulators)
        {
            var accumulator = accumulators.Current;

            if (!enumerateDirectory && directoriesToSkipRecursively == 0)
            {
                var files = FindMatches(m_pathToContent.Keys, AbsolutePath.Create(m_pathTable, directoryPath), pattern, false);
                foreach (var file in files)
                {
                    accumulator.AddFile(file.GetName(m_pathTable).ToString(m_pathTable.StringTable));
                    accumulator.AddTrackFile(file.GetName(m_pathTable).ToString(m_pathTable.StringTable), FileAttributes.Normal);
                }
            }

            if (recursive || enumerateDirectory || directoriesToSkipRecursively > 0)
            {
                var directories = FindMatches(
                    m_directories,
                    AbsolutePath.Create(m_pathTable, directoryPath),
                    enumerateDirectory ? pattern : "*",
                    false);

                foreach (var directory in directories)
                {
                    var dirName = directory.GetName(m_pathTable).ToString(m_pathTable.StringTable);
                    if (enumerateDirectory && directoriesToSkipRecursively == 0)
                    {
                        accumulator.AddFile(dirName);
                    }

                    accumulator.AddTrackFile(dirName, FileAttributes.Directory);

                    if ((recursive || directoriesToSkipRecursively > 0))
                    {
                        accumulators.AddNew(accumulator, dirName);
                        var recurs = EnumerateDirectoryEntries(
                            Path.Combine(directoryPath, dirName),
                            enumerateDirectory,
                            pattern,
                            directoriesToSkipRecursively == 0 ? 0 : directoriesToSkipRecursively - 1,
                            recursive,
                            accumulators);

                        if (!recurs.Succeeded)
                        {
                            return recurs;
                        }
                    }
                }
            }

            return new EnumerateDirectoryResult(
                directoryPath,
                EnumerateDirectoryStatus.Success,
                (int)NativeIOConstants.ErrorNoMoreFiles);
        }

        /// <summary>
        /// Returns all paths, including both files and directories
        /// </summary>
        public IEnumerable<AbsolutePath> AllPaths()
        {
            return m_directories.Union(m_pathToContent.Keys);
        }

        private IEnumerable<AbsolutePath> FindMatches(IEnumerable<AbsolutePath> candidatePaths, AbsolutePath root, string pattern, bool recursive)
        {
            var regex = CreateRegexFromPattern(pattern);

            return candidatePaths.Where(
                path =>
                {
                    if (recursive ? path.IsWithin(m_pathTable, root) : (path.GetParent(m_pathTable).Equals(root)))
                    {
                        var baseName = GetBaseName(path);
                        if (regex.Match(baseName).Success)
                        {
                            return true;
                        }
                    }

                    return false;
                });
        }

        private void AddAllUpstreamDirectories(AbsolutePath path)
        {
            while (path.IsValid)
            {
                m_directories.Add(path);
                path = path.GetParent(m_pathTable);
            }
        }

        // NOTE: the search pattern used for glob is not a regex. It can only contain
        // valid path literals and wildcards * and ?. We use regex here for pattern matching
        // in test code, with the assumption being that no other wildcards are specified in the pattern.
        private static Regex CreateRegexFromPattern(string pattern)
        {
            if (pattern == "*.*")
            {
                pattern = "*";
            }

            var regexPattern = "^" + Regex.Escape(pattern)
                                   .Replace(@"\*", ".*")
                                   .Replace(@"\?", ".") + "$";

            return new Regex(regexPattern);
        }

        /// <summary>
        /// Gets a string representation for a current instance.
        /// </summary>
        public string ToDebuggerDisplay()
        {
            var stringBuilder = new StringBuilder();

            foreach (var directory in m_directories)
            {
                var filesInDirectory = EnumerateFiles(directory).ToList();
                foreach (var file in filesInDirectory)
                {
                    stringBuilder.AppendLine($"{file.ToString(m_pathTable)}");
                }
            }

            return stringBuilder.ToString();
        }
    }
}
