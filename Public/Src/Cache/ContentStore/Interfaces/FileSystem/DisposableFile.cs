// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
            _context = context;
            _fileSystem = fileSystem;
            Path = filePath;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                // No need to check for existence, DeleteFile is a no-op if the file does not exist.
                _fileSystem.DeleteFile(Path);
            }
            catch (Exception exception)
            {
                _context.Error(exception, $"Unable to cleanup `{Path}`", component: nameof(DisposableFile));
            }
        }
    }
}
