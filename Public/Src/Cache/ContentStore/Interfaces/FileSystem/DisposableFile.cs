// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <nodoc />
    public sealed class DisposableFile : IDisposable
    {
        private readonly Context _context;
        private readonly IAbsFileSystem _fileSystem;

        /// <nodoc />
        public AbsolutePath Path { get; }

        /// <nodoc />
        public DisposableFile(Context context, IAbsFileSystem fileSystem, AbsolutePath filePath)
        {
            Contract.RequiresNotNull(context);
            Contract.RequiresNotNull(fileSystem);
            Contract.RequiresNotNull(filePath);
            _context = context;
            _fileSystem = fileSystem;
            Path = filePath;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                if (_fileSystem.FileExists(Path))
                {
                    _fileSystem.DeleteFile(Path);
                }
            }
            catch (Exception exception)
            {
                _context.Debug($"Unable to cleanup `{Path}` due to exception: {exception}");
            }
        }
    }
}
