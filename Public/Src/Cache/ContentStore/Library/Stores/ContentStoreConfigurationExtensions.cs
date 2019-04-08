// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     ContentStoreConfiguration extension methods.
    /// </summary>
    public static class ContentStoreConfigurationExtensions
    {
        /// <summary>
        ///     Name of the standard JSON file in a cas root directory.
        /// </summary>
        public const string FileName = "cas.json";

        /// <summary>
        ///     Check if a valid content store configuration is present in a CAS root directory.
        /// </summary>
        /// <summary>
        ///     Deserialize a ContentStoreConfiguration from JSON in the standard filename in a CAS root directory.
        /// </summary>
        public static async Task<ObjectResult<ContentStoreConfiguration>> ReadContentStoreConfigurationAsync(
            this IAbsFileSystem fileSystem, AbsolutePath rootPath)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            AbsolutePath jsonPath = rootPath / FileName;
            ContentStoreConfiguration configuration;

            if (!fileSystem.DirectoryExists(rootPath))
            {
                return new ObjectResult<ContentStoreConfiguration>($"Directory path=[{rootPath}] does not exist");
            }

            if (!fileSystem.FileExists(jsonPath))
            {
                return new ObjectResult<ContentStoreConfiguration>($"ContentStoreConfiguration not present at path=[{jsonPath}]");
            }

            using (var stream = await fileSystem.OpenReadOnlySafeAsync(jsonPath, FileShare.None))
            {
                configuration = stream.DeserializeFromJSON<ContentStoreConfiguration>();
            }

            if (!configuration.IsValid)
            {
                return new ObjectResult<ContentStoreConfiguration>($"Invalid content store configuration at path=[{jsonPath}]");
            }

            return new ObjectResult<ContentStoreConfiguration>(configuration);
        }

        /// <summary>
        ///     Serialize a ContentStoreConfiguration to JSON in the standard filename in a CAS root directory.
        /// </summary>
        public static async Task Write(this ContentStoreConfiguration configuration, IAbsFileSystem fileSystem, AbsolutePath rootPath)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);
            Contract.Requires(configuration != null);
            Contract.Requires(configuration.IsValid);

            AbsolutePath jsonPath = rootPath / FileName;

            if (!fileSystem.DirectoryExists(rootPath))
            {
                throw new CacheException($"Directory path=[{rootPath}] does not exist");
            }

            using (var stream =
                await fileSystem.OpenSafeAsync(jsonPath, FileAccess.Write, FileMode.Create, FileShare.None))
            {
                configuration.SerializeToJSON(stream);
            }
        }

        /// <summary>
        ///     Extract quota hard and soft limits from an expression.
        /// </summary>
        public static Tuple<string, string> ExtractHardSoft(this string expression)
        {
            if (string.IsNullOrEmpty(expression))
            {
                throw new CacheException("Empty hard/soft expression is not valid");
            }

            var segments = expression.Split(':');
            if (segments.Length != 1 && segments.Length != 2)
            {
                throw new CacheException($"Invalid hard/soft expression=[{expression}]");
            }

            var hard = segments[0];
            var soft = segments.Length == 2 ? segments[1] : null;

            return Tuple.Create(hard, soft);
        }
    }
}
