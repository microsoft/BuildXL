// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    ///     A class that helps store a persistent id file.
    /// </summary>
    public static class PersistentId
    {
        /// <summary>
        ///     Human-readable format with just the raw hex string, no punctuation.
        /// </summary>
        private const string SerializationFormat = "N";

        /// <summary>
        ///     Load value from the filesystem.
        /// </summary>
        public static Guid Load(IAbsFileSystem fileSystem, AbsolutePath filePath)
        {
            Guid guid;
            try
            {
                // First try reading the GUID file
                guid = Read(fileSystem, filePath);
            }
            catch (Exception e) when (e is CacheException || e is IOException)
            {
                // If that fails, we likely need to create a Guid
                guid = CacheDeterminism.NewCacheGuid();
                try
                {
                    fileSystem.CreateDirectory(filePath.Parent);

                    // Write the Guid file
                    fileSystem.WriteAllBytes(filePath, Encoding.UTF8.GetBytes(guid.ToString(SerializationFormat)));
                }
                catch (Exception ex) when (ex is IOException)
                {
                    // If we failed to write the Guid file we may have just missed getting the guid in the first place,
                    // so let us try to read it again.  This failure we let go all the way out.
                    guid = Read(fileSystem, filePath);
                }
            }

            return guid;
        }

        private static Guid Read(IAbsFileSystem fileSystem, AbsolutePath filePath)
        {
            var bytes = fileSystem.ReadAllBytes(filePath);
            var idString = Encoding.UTF8.GetString(bytes);
            if (!Guid.TryParseExact(idString, SerializationFormat, out var result))
            {
                fileSystem.DeleteFile(filePath);
                throw new CacheException("Cache id file was present but not in the correct format", filePath.Path);
            }

            return result;
        }
    }
}
