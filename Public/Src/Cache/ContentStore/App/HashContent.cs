// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Synchronization;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     Hash some files
        /// </summary>
        [Verb(Aliases = "hf", Description = "Hash content files")]
        public void HashFile([Required] string path, [Description(HashTypeDescription)] string hashType)
        {
            Initialize();

            var ht = GetHashTypeByNameOrDefault(hashType);
            var absolutePath = new AbsolutePath(Path.GetFullPath(path));
            var paths = new List<AbsolutePath>();

            if (_fileSystem.DirectoryExists(absolutePath))
            {
                paths.AddRange(_fileSystem.EnumerateFiles(absolutePath, EnumerateOptions.Recurse).Select(fileInfo => fileInfo.FullPath));
            }
            else if (_fileSystem.FileExists(absolutePath))
            {
                paths.Add(absolutePath);
            }
            else
            {
                throw new ArgumentException("given path is not an existing file or directory");
            }

            foreach (var p in paths)
            {
                var contentHash = TaskSafetyHelpers.SyncResultOnThreadPool(() => _fileSystem.CalculateHashAsync(p, ht));
                _logger.Always($"{contentHash} {p}");
            }
        }
    }
}
