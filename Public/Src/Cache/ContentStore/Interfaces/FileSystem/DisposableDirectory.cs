// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// A temp directory that is recursively deleted on disposal
    /// </summary>
    public sealed class DisposableDirectory : IDisposable
    {
        private readonly IAbsFileSystem _fileSystem;

        /// <summary>
        /// Gets path to the directory
        /// </summary>
        public AbsolutePath Path { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DisposableDirectory" /> class.
        /// </summary>
        public DisposableDirectory(IAbsFileSystem fileSystem)
            : this(fileSystem, GetRandomFileName())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DisposableDirectory" /> class.
        /// </summary>
        public DisposableDirectory(IAbsFileSystem fileSystem, string subpathSuffix)
            : this(fileSystem, CreateRootPath(fileSystem, subpathSuffix))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DisposableDirectory" /> class.
        /// </summary>
        public DisposableDirectory(IAbsFileSystem fileSystem, AbsolutePath directoryPath)
        {
            Contract.RequiresNotNull(fileSystem);
            _fileSystem = fileSystem;
            Path = directoryPath;
            fileSystem.CreateDirectory(directoryPath);
        }

        private static AbsolutePath CreateRootPath(IAbsFileSystem fileSystem, string subpathSuffix)
        {
            var path = fileSystem.GetTempPath() / "CloudStore" / subpathSuffix;
            return path;
        }

        /// <summary>
        /// Create path to a randomly named file inside this directory.
        /// </summary>
        public AbsolutePath CreateRandomFileName() => AbsolutePath.CreateRandomFileName(Path);

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                if (_fileSystem.DirectoryExists(Path))
                {
                    _fileSystem.DeleteDirectory(Path, DeleteOptions.All);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Unable to cleanup due to exception: {0}", exception);
            }
        }

        private static string GetRandomFileName()
        {
            // Don't use Path.GetRandomFileName(), it's not random enough when running multi-threaded.
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }
    }
}
