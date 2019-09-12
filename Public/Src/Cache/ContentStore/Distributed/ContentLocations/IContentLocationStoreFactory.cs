// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

// ReSharper disable All
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
        Task<IContentLocationStore> CreateAsync(MachineLocation localMachineLocation);
    }
}
