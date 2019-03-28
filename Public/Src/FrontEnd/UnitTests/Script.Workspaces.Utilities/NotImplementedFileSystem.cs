// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using IFileSystem = BuildXL.FrontEnd.Sdk.FileSystem.IFileSystem;

namespace Test.DScript.Workspaces.Utilities
{
    /// <summary>
    /// Throws a <see cref="NotImplementedException"/> on every method
    /// </summary>
    /// <remarks>
    /// Used for injecting unhandled failures in the parsing queue
    /// </remarks>
    public sealed class NotImplementedFileSystem : IFileSystem
    {
        /// <inheritdoc />
        public IFileSystem CopyWithNewPathTable(PathTable pathTable)
        {
            throw new NotImplementedException();
        }

        /// <nodoc/>
        public StreamReader OpenText(AbsolutePath path)
        {
            Contract.Requires(Exists(path));
            throw new NotImplementedException();
        }

        /// <nodoc/>
        public bool Exists(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            throw new NotImplementedException();
        }

        /// <nodoc/>
        public bool IsDirectory(AbsolutePath path)
        {
            Contract.Requires(Exists(path));
            throw new NotImplementedException();
        }

        /// <nodoc/>
        public string GetBaseName(AbsolutePath path)
        {
            Contract.Requires(Exists(path));
            throw new NotImplementedException();
        }

        /// <nodoc/>
        public PathTable GetPathTable()
        {
            return new PathTable();
        }

        /// <nodoc/>
        public IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, string pattern = "*", bool recursive = false)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(IsDirectory(path));
            Contract.Requires(!string.IsNullOrEmpty(pattern));
            throw new NotImplementedException();
        }

        /// <nodoc/>
        public IEnumerable<AbsolutePath> EnumerateFiles(AbsolutePath path, string pattern = "*", bool recursive = false)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(IsDirectory(path));
            Contract.Requires(!string.IsNullOrEmpty(pattern));
            throw new NotImplementedException();
        }
        /// <nodoc/>
        public EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool enumerateDirectory,
            string pattern,
            uint directoriesToSkipRecursively,
            bool recursive,
            IDirectoryEntriesAccumulator accumulators)
        {
            throw new System.NotImplementedException();
        }
    }
}
