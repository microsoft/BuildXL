// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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