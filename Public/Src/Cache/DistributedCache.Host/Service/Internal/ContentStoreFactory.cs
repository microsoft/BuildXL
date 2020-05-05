// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
#nullable enable
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
            ContentStoreSettings? contentStoreSettings,
            IDistributedLocationStore? distributedStore,
            ConfigurationModel? configurationModel = null)
            => new FileSystemContentStore(
                fileSystem, SystemClock.Instance, rootPath, configurationModel, distributedStore, contentStoreSettings);
    }
}
