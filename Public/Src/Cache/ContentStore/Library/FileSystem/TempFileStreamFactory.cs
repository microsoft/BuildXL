// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.FileSystem
{
    /// <summary>
    ///     Generator of temp file streams that automatically remove their backing file on disposal.
    /// </summary>
    public sealed class TempFileStreamFactory : IDisposable
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly DisposableDirectory _directory;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TempFileStreamFactory"/> class.
        /// </summary>
        public TempFileStreamFactory(IAbsFileSystem fileSystem)
        {
            Contract.Requires(fileSystem != null);

            _fileSystem = fileSystem;
            _directory = new DisposableDirectory(_fileSystem);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _directory.Dispose();
        }

        /// <summary>
        ///     Create a new instance from content in the given input stream.
        /// </summary>
        public FileStream Create(Context context, Stream stream, long size = -1)
        {
            const int bufSize = FileSystemConstants.FileIOBufferSize;
            var path = _directory.CreateRandomFileName();

            try
            {
                using (var fileStream = new FileStream(path.Path, FileMode.Create, FileAccess.Write, FileShare.None, bufSize))
                {
                    stream.CopyTo(fileStream, bufSize, size);
                }

                return new FileStream(
                    path.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufSize, FileOptions.DeleteOnClose);
            }
            catch (Exception exception)
            {
                context.Error($"Failed to create temp file stream for path=[{path}] due to error=[{exception.GetLogEventMessage()}]");

                try
                {
                    _fileSystem.DeleteFile(path);
                }
                catch (Exception e)
                {
                    context.Error($"Failed to delete temp file at path=[{path}] due to error=[{e.GetLogEventMessage()}]");
                }

                throw;
            }
        }
    }
}
