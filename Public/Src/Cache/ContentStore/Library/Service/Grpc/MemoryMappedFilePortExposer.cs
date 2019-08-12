// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.MemoryMappedFiles;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Exposes the port used for GRPC through a non-persistent memory mapped file.
    /// </summary>
    public class MemoryMappedFilePortExposer : IPortExposer
    {
        private static readonly long FileSize = 10;   // Only long enough to store the port number.
        private MemoryMappedFile _file;

        private readonly string _baseFileName;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryMappedFilePortExposer"/> class.
        /// </summary>
        /// <param name="fileName">Name of the memory-mapped file to use</param>
        /// <param name="logger"></param>
        public MemoryMappedFilePortExposer(string fileName, ILogger logger)
        {
            _baseFileName = fileName;
            _logger = logger;
        }

        /// <inheritdoc />
        public IDisposable Expose(int port)
        {
            Contract.Requires(port > 0 && port <= 65535); // Limit on computers' ports.

#if PLATFORM_WIN
            var fileName = $@"Global\{_baseFileName}";
            try
            {
                _file = CreateMemoryMappedFile(port, fileName);
            }
            catch (UnauthorizedAccessException)
            {
                fileName = _baseFileName;
                _logger.Warning(
                    $"Failed to create global memory mapped file {fileName}. MMF will only be available to the current user. If global access is required, try running as administrator.");
                _file = CreateMemoryMappedFile(port, fileName);
            }
#else
            // OSX can't use global memory mapped files, so don't use the "Global" prefix
            var fileName = _baseFileName;
            _file = CreateMemoryMappedFile(port, _baseFileName);
            System.Console.WriteLine("Created memory mapped file at " + _baseFileName);
#endif

            return new MemoryMappedFileDisposer(_logger, _file, fileName);
        }

        private MemoryMappedFile CreateMemoryMappedFile(int port, string fileName)
        {
            _logger.Always($"Trying to expose GRPC port {port} at memory mapped file '{fileName}'");

#if PLATFORM_WIN
            var file = MemoryMappedFile.CreateNew(
                fileName,
                FileSize,
                MemoryMappedFileAccess.ReadWrite);
#else
            var file = MemoryMappedFile.CreateFromFile(
                fileName,
                FileMode.Create,
                null,
                FileSize,
                MemoryMappedFileAccess.ReadWrite);
#endif

            try
            {
                using (var stream = file.CreateViewStream())
                {
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(port);
                    }
                }
            }
            catch
            {
                file.Dispose();
                throw;
            }

            _logger.Always($"Memory mapped file '{fileName}' successfully created");

            return file;
        }

        private class MemoryMappedFileDisposer : IDisposable
        {
            private readonly ILogger _logger;
            private readonly MemoryMappedFile _file;
            private readonly string _fileName;
            private bool _isDisposed;

            public MemoryMappedFileDisposer(ILogger logger, MemoryMappedFile file, string fileName)
            {
                _logger = logger;
                _file = file;
                _fileName = fileName;
            }

            public void Dispose()
            {
                if (!_isDisposed)
                {
                    _logger.Debug($"Closing memory mapped file '{_fileName}'");
                    _file.Dispose();
                    _isDisposed = true;
                }
            }
        }
    }
}
