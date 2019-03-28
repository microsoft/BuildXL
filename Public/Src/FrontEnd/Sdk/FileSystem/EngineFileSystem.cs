// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using IFileSystem = BuildXL.FrontEnd.Sdk.FileSystem.IFileSystem;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Provides an abstraction layer around  <see cref="FrontEndEngineAbstraction"/> to
    /// expose file system operations.
    /// </summary>
    /// <remarks>
    /// Workspaces interact with the filesystem through the <see cref="IFileSystem"/> abstraction.
    /// This class is meant for both production/test code that needs to interface with the real file system.
    /// </remarks>
    public sealed class EngineFileSystem : PassThroughFileSystem, IFileSystem
    {
        private readonly FrontEndEngineAbstraction m_engine;

        /// <summary>
        /// Create a simple wrapper representing the real file system
        /// </summary>
        public EngineFileSystem(PathTable pathTable, FrontEndEngineAbstraction engine)
            : base(pathTable)
        {
            Contract.Requires(pathTable != null);
            m_engine = engine;
        }

        /// <inheritdoc/>
        public override bool Exists(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return m_engine.FileExists(path);
        }

        /// <inheritdoc/>
        IFileSystem IFileSystem.CopyWithNewPathTable(PathTable pathTable)
        {
            return new EngineFileSystem(pathTable, m_engine);
        }

        /// <inheritdoc/>
        public override StreamReader OpenText(AbsolutePath path)
        {
            Contract.Requires(Exists(path));
            if (!m_engine.TryGetFrontEndFile(path, string.Empty, out var stream))
            {
                throw new BuildXLException("Could not get content for file " + path.ToString(PathTable));
            }

            return new StreamReader(stream);
        }

        /// <inheritdoc/>
        public override IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, string pattern = "*", bool recursive = false)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(IsDirectory(path));
            Contract.Requires(!string.IsNullOrEmpty(pattern));

            return m_engine.EnumerateEntries(path, pattern, recursive, directories: true);
        }

        /// <inheritdoc/>
        public override IEnumerable<AbsolutePath> EnumerateFiles(AbsolutePath path, string pattern = "*", bool recursive = false)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(IsDirectory(path));
            Contract.Requires(!string.IsNullOrEmpty(pattern));

            return m_engine.EnumerateEntries(path, pattern, recursive, directories: false);
        }

        /// <inheritdoc/>
        public override bool IsDirectory(AbsolutePath path)
        {
            return m_engine.DirectoryExists(path);
        }
    }
}
