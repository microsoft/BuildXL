// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Configuration objects are typically created from a user-specified JSON represented with <see cref="ICacheConfigData"/> via <see cref="CacheFactory.Create{T}(ICacheConfigData, Guid?, IConfiguration)"/>. Some
    /// configuration objects may have settings that are not specified by the user, but injected by the build engine. This interface represent those objects, and the creation method in the cache factory will
    /// make sure to call this interface on those cases, in order to give the configuration object to populate those settings from the <see cref="IConfiguration"/>.
    /// </summary>
    public interface IEngineDependentSettingsConfiguration
    {
        /// <summary>
        /// Configuration objects populate settings coming from the engine side, represented with a <see cref="IConfiguration"/>
        /// </summary>
        bool TryPopulateFrom(Guid activityId, IConfiguration configuration, out Failure failure);
    }
}
