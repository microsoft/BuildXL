// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Provides access to services
    /// </summary>
    public interface IServicesProvider
    {
        /// <nodoc />
        bool TryGetService<TService>(out TService? service);
    }

    /// <summary>
    /// Services available to host
    /// </summary>
    public interface ICacheServerServices
    {
        /// <nodoc />
        IPushFileHandler PushFileHandler { get; }

        /// <nodoc />
        IDistributedStreamStore StreamStore { get; }
    }
}
