// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Stores;

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// A factory for creating content location store.
    /// </summary>
    /// <remarks>
    /// A factory class for creating content location stores.
    /// This allows the stores to be passed in opaquely to the ContentSession for instantiation
    /// without the session having knowledge on how to construct a specific content location store.
    /// </remarks>
    public interface IContentLocationStoreFactory : IStartupShutdown
    {
        /// <summary>
        /// Creates and returns a file location cache for the given session.
        /// </summary>
        Task<IContentLocationStore> CreateAsync(MachineLocation localMachineLocation, ILocalContentStore? localContentStore);
    }
}
