// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Exposes common members between master and worker distribution services.
    /// </summary>
    public interface IDistributionService : IDisposable
    {
        /// <summary>
        /// Initializes the distribution service.
        /// </summary>
        /// <returns>True if initialization completed successfully. Otherwise, false.</returns>
        bool Initialize();
    }
}