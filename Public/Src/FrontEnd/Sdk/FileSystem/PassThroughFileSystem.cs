// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Sdk.FileSystem
{
    /// <summary>
    /// Provides layer around File System (System.IO.File, System.IO.Directory)
    /// </summary>
    public class PassThroughFileSystem : IFileSystem
    {
        /// <nodoc />
        protected PathTable PathTable { get; private set; }

        /// <inheritdoc />
        IFileSystem IFileSystem.CopyWithNewPathTable(PathTable pathTable)
        {
            // It is safe to just return a new filesystem because this class does not store any absolute paths.
            return new PassThroughFileSystem(pathTable);
        }

        /// <summary>
        /// Creates a simple wrapper for the file system.
        /// </summary>
        public PassThroughFileSystem(PathTable pathTable)
        {
            this.PathTable = pathTable;
        }

        /// <inheritdoc />
        public virtual IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, [JetBrains.Annotations.NotNull] string pattern = "*", bool recursive = false)
        {
            return EnumerateHelper(path, pattern, recursive, directories: true);
        }

        /// <nodoc />
        public virtual IEnumerable<AbsolutePath> EnumerateFiles(AbsolutePath path, [JetBrains.Annotations.NotNull] string pattern = "*", bool recursive = false)
        {
            return EnumerateHelper(path, pattern, recursive, directories: false);
        }

        private IEnumerable<AbsolutePath> EnumerateHelper(AbsolutePath path, [JetBrains.Annotations.NotNull] string pattern = "*", bool recursive = false, bool directories = false)
        {
            var entries = new List<AbsolutePath>();

            var result = FileUtilities.EnumerateDirectoryEntries(
                path.ToString(this.PathTable),
                recursive,
                pattern,
                (currentDirectory, name, attr) =>
                {
                    if ((attr & FileAttributes.Directory) != 0 == directories)
                    {
                        var fullName = Path.Combine(currentDirectory, name);
                        var fullNamePath = AbsolutePath.Create(this.PathTable, fullName);
                        Contract.Assert(fullNamePath.IsValid);

                        entries.Add(fullNamePath);
                    }
                });

            if (
                !(result.Status == EnumerateDirectoryStatus.Success ||
                  result.Status == EnumerateDirectoryStatus.SearchDirectoryNotFound))
            {
                throw new BuildXLException(I($"Error enumerating path '{path.ToString(this.PathTable)}'."), result.CreateExceptionForError());
            }

            return entries;
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
            return FileUtilities.EnumerateDirectoryEntries(
                directoryPath,
                enumerateDirectory,
                pattern,
                directoriesToSkipRecursively,
                recursive,
                accumulators);
        }

        /// <inheritdoc />
        public virtual bool Exists(AbsolutePath path)
        {
            // Other files that use this have contracts. Do I need to do this here?
            //I.E Contract.Requires(path.IsValid);
            return File.Exists(path.ToString(GetPathTable()));
        }

        /// <inheritdoc />
        public string GetBaseName(AbsolutePath path)
        {
            return path.GetName(this.PathTable).ToString(this.PathTable.StringTable);
        }

        /// <inheritdoc />
        public PathTable GetPathTable()
        {
            return this.PathTable;
        }

        /// <inheritdoc />
        public virtual bool IsDirectory(AbsolutePath path)
        {
            return Directory.Exists(path.ToString(GetPathTable()));
        }

        /// <inheritdoc />
        public virtual StreamReader OpenText(AbsolutePath path)
        {
            Contract.Requires(Exists(path));
            return new StreamReader(path.ToString(this.PathTable));
        }
    }
}
