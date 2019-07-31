// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.IO.MemoryMappedFiles;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Reads a memory mapped file to look for a shared GRPC port.
    /// </summary>
    public class MemoryMappedFilePortReader : IPortReader
    {
        private readonly string _fileName;
        private readonly ILogger _logger;

        /// <nodoc />
        public MemoryMappedFilePortReader(string fileName, ILogger logger)
        {
            _fileName = fileName;
            _logger = logger;
        }
        
        /// <summary>
        /// Reads and returns the port to use for GRPC by reading the default memory mapped file.
        /// </summary>
        /// <returns>The GRPC port to use.</returns>
        public int ReadPort()
        {
            string content = string.Empty;

#if PLATFORM_WIN
            try
            {
                using (var file = GetMemoryMappedFile(_fileName))
                using (var stream = file.CreateViewStream(0, 0, MemoryMappedFileAccess.Read))
                {
                    using (var reader = new StreamReader(stream))
                    {
                        content = reader.ReadLine();
                    }
                }
            }
            catch (FileNotFoundException)
            {
                throw new PortReaderException(
                    $"The memory-mapped file '{_fileName}' was not found during GRPC port recognition." +
                    "Possible cause: Server is not available, or file was created without administrator permissions under a different user.");
            }
#else
            try
            {
                using (FileStream fileStream = File.OpenRead(_fileName))
                using (StreamReader reader = new StreamReader(fileStream))
                {
                    content = reader.ReadLine();
                }
            }
            catch (FileNotFoundException)
            {
                throw new PortReaderException(
                    $"The memory-mapped file '{_fileName}' was not found during GRPC port recognition." +
                    "Possible cause: Server is not available, or file was created without administrator permissions under a different user.");
            }
#endif

            if (ushort.TryParse(content, out ushort result))
            {
                return result;
            }

            throw new PortReaderException($"Content of memory mapped file '{_fileName}' is not a valid port number. Content: {content}");
        }
        
        private MemoryMappedFile GetMemoryMappedFile(string fileName)
        {
            try
            {
                var globalName = $@"Global\{fileName}";
                _logger.Debug($"Trying to open memory mapped file '{globalName}'");
                return MemoryMappedFile.OpenExisting(globalName, MemoryMappedFileRights.Read);
            }
            catch (FileNotFoundException)
            {
                // Maybe the MMF exposer didn't have admin permissions and ended up creating a user-specific MMF.
                _logger.Debug($"Global memory mapped file was not found. Trying to open memory mapped file '{fileName}'");
                return MemoryMappedFile.OpenExisting(fileName, MemoryMappedFileRights.Read);
            }
        }
    }
}
