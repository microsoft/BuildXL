// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BuildXL.Cache.BuildCacheResource.Model;

namespace BuildXL.Cache.BuildCacheResource.Helper
{
    /// <summary>
    /// Facilities to interpret a 1ES Build Cache resource configuration file associated with a 1ESHP
    /// </summary>
    public static class BuildCacheResourceHelper
    {

        /// <summary>
        /// Loads the content of the given JSON file and returns a representation of it
        /// </summary>
        public static async Task<HostedPoolBuildCacheConfiguration> LoadFromJSONAsync(string pathToJson)
        {
            Contract.Requires(!string.IsNullOrEmpty(pathToJson));
            using FileStream openStream = File.OpenRead(pathToJson);

            return await LoadFromJSONAsync(openStream);
        }

        /// <summary>
        /// <see cref="LoadFromJSONAsync(string)"/>
        /// </summary>
        public static async Task<HostedPoolBuildCacheConfiguration> LoadFromJSONAsync(Stream content)
        {
            var buildCaches = await JsonSerializer.DeserializeAsync<IReadOnlyCollection<BuildCacheConfiguration>>(content);

            if (buildCaches is null)
            {
                throw new ArgumentNullException(nameof(content), "The containing JSON is null");
            }

            return new HostedPoolBuildCacheConfiguration { AssociatedBuildCaches = buildCaches! };
        }

        /// <summary>
        /// <see cref="LoadFromJSONAsync(string)"/>
        /// </summary>
        public static HostedPoolBuildCacheConfiguration LoadFromString(string content)
        {
            var buildCaches = JsonSerializer.Deserialize<IReadOnlyCollection<BuildCacheConfiguration>>(content);

            if (buildCaches is null)
            {
                throw new ArgumentNullException(nameof(content), "The containing JSON is null");
            }

            return new HostedPoolBuildCacheConfiguration { AssociatedBuildCaches = buildCaches! };
        }

        /// <summary>
        /// Given a <paramref name="hostedPoolBuildCacheConfiguration"/>, selects the first cache in the collection if <paramref name="hostedPoolActiveBuildCacheName"/> is null. Otherwise, tries to match the given active build
        /// container name against the cache configuration to retrieve the explicitly selected one.
        /// </summary>
        public static bool TrySelectBuildCache(this HostedPoolBuildCacheConfiguration hostedPoolBuildCacheConfiguration, string? hostedPoolActiveBuildCacheName, out BuildCacheConfiguration? buildCacheConfiguration)
        {
            // If an active cache is not provided, the first one in the collection of associated caches is the default
            if (string.IsNullOrEmpty(hostedPoolActiveBuildCacheName))
            {
                buildCacheConfiguration = hostedPoolBuildCacheConfiguration!.AssociatedBuildCaches.First();
                return true;
            }

            // Otherwise, look for it using the given name
            buildCacheConfiguration = hostedPoolBuildCacheConfiguration.AssociatedBuildCaches.FirstOrDefault(buildCacheConfig => buildCacheConfig.Name == hostedPoolActiveBuildCacheName);

            return buildCacheConfiguration is not null;
        }
    }
}
