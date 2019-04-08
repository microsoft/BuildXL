// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using IFileSystem = BuildXL.FrontEnd.Sdk.FileSystem.IFileSystem;

namespace Test.DScript.Workspaces.Utilities
{
    /// <summary>
    /// A file system based directly on a <see ref="ModuleRepository"/>.
    /// </summary>
    public sealed class ModuleRepositoryFileSystem : IFileSystem
    {
        private readonly ModuleRepository m_moduleRepository;
        private PathTable m_pathTable;

        /// <nodoc/>
        public ModuleRepositoryFileSystem(PathTable pathTable, params ModuleRepository[] moduleRepositoryArray)
        {
            Contract.Requires(moduleRepositoryArray.Length > 0);
            Contract.Requires(moduleRepositoryArray.Select(repo => repo.RootDir).Distinct().Count() == 1, "All repositories must have the same root dir");

            // Traverse the sequence
            var aggregatedContent =
                moduleRepositoryArray.Aggregate(
                    seed: new ModuleRepository(pathTable, moduleRepositoryArray.First().RootDir),
                    func: (acum, modulesWithContent) => acum.AddAll(modulesWithContent));

            m_moduleRepository = aggregatedContent;
            m_pathTable = pathTable;
        }

        /// <inheritdoc />
        public IFileSystem CopyWithNewPathTable(PathTable pathTable)
        {
            // These tests should not reload the graph.
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public StreamReader OpenText(AbsolutePath path)
        {
            Contract.Requires(Exists(path));
            var content = m_moduleRepository.GetSpecContentFromPath(path);

            MemoryStream memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            StreamReader stream = null;

            try
            {
                stream = new StreamReader(memoryStream);
                memoryStream = null;
            }
            finally
            {
                if (memoryStream != null)
                {
                    memoryStream.Dispose();
                }
            }

            return stream;
        }

        /// <inheritdoc/>
        public bool Exists(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return m_moduleRepository.ContainsSpec(path);
        }

        /// <inheritdoc/>
        public bool IsDirectory(AbsolutePath path)
        {
            Contract.Requires(Exists(path));
            return false;
        }

        /// <inheritdoc/>
        public string GetBaseName(AbsolutePath path)
        {
            Contract.Requires(Exists(path));
            return path.ToString(m_pathTable, PathFormat.HostOs);
        }

        /// <inheritdoc/>
        public PathTable GetPathTable()
        {
            return m_pathTable;
        }

        /// <nodoc/>
        public IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, string pattern = "*", bool recursive = false)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(IsDirectory(path));
            Contract.Requires(!string.IsNullOrEmpty(pattern));
            throw new System.NotImplementedException();
        }

        /// <nodoc/>
        public IEnumerable<AbsolutePath> EnumerateFiles(AbsolutePath path, string pattern = "*", bool recursive = false)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(IsDirectory(path));
            Contract.Requires(!string.IsNullOrEmpty(pattern));
            throw new System.NotImplementedException();
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
