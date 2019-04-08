// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.Host.Service.Internal
{
    public class ContentStoreFactory
    {
        /// <summary>
        /// Creates a <see cref="FileSystemContentStore"/> at <see cref="rootPath"/>
        /// </summary>
        public static IContentStore CreateContentStore(
            IAbsFileSystem fileSystem,
            AbsolutePath rootPath,
            NagleQueue<ContentHash> evictionAnnouncer,
            DistributedEvictionSettings distributedEvictionSettings,
            ContentStoreSettings contentStoreSettings,
            TrimBulkAsync trimBulkAsync,
            ConfigurationModel configurationModel = null)
            => new FileSystemContentStore(
                fileSystem, SystemClock.Instance, rootPath, configurationModel, evictionAnnouncer, distributedEvictionSettings, trimBulkAsync, contentStoreSettings);
    }
}
