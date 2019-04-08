// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     Create verb.
        /// </summary>
        [Verb(Description = "Create a fresh local content cache")]
        internal void Create
            (
            [Required, Description("Content cache root directory")] string root,
            [Required, Description(MaxSizeDescription)] string maxSize,
            [Description(HashTypeDescription)] string hashType,
            [DefaultValue(0), Description("Percentage of total size to populate. Mutually exclusive with fileCount.")] int percent,
            [DefaultValue(0), Description("Number of files to populate. Mutually exclusive with percent.")] int fileCount,
            [DefaultValue("4KB"), Description("Size of each populated file.")] string fileSize,
            [DefaultValue(false), Description("Whether to treat fileSize as exact or maximum.")] bool useExactSize
            )
        {
            Initialize();

            var ht = GetHashTypeByNameOrDefault(hashType);
            var rootPath = new AbsolutePath(root);
            if (_fileSystem.DirectoryExists(rootPath))
            {
                throw new CacheException($"Root path=[{rootPath}] must not already exist.");
            }

            _fileSystem.CreateDirectory(rootPath);

            var configuration = new ContentStoreConfiguration(maxSize, null);
            configuration.Write(_fileSystem, new AbsolutePath(root)).Wait();

            RunFileSystemContentStore(rootPath, async (context, session) =>
            {
                if (percent > 0)
                {
                    var size = configuration.MaxSizeQuota.Soft;
                    await session.PutRandomAsync(context, ht, false, size, percent, fileSize.ToSize(), useExactSize).ConfigureAwait(false);
                }
                else if (fileCount > 0)
                {
                    await session.PutRandomAsync(context, ht, false, fileCount, fileSize.ToSize(), useExactSize).ConfigureAwait(false);
                }
            });
        }
    }
}
