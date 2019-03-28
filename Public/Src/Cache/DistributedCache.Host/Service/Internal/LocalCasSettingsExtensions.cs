// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Host.Service.Internal
{
    public static class LocalCasSettingsExtension
    {
        public const string DefaultScenario = "ContentAddressableStore";

        /// <summary>
        /// Filters out <see cref="NamedCacheSettings"/> entries whose capability demands are not met by the host.
        /// Also filters out entries pointing to e physical drive that doesn't exist.
        /// </summary>
        /// <param name="this"></param>
        /// <param name="hostCapabilities">Capabilities provided by the host</param>
        /// <param name="logger"></param>
        /// <param name="driveExistenceOverride">For testing purposes</param>
        /// <returns></returns>
        public static LocalCasSettings FilterUnsupportedNamedCaches(
            this LocalCasSettings @this, IEnumerable<string> hostCapabilities, ILogger logger, Predicate<string> driveExistenceOverride = null)
        {
            Predicate<string> checkDriveExists = driveExistenceOverride ?? Directory.Exists;

            var result = new LocalCasSettings
            {
                CasClientSettings = @this.CasClientSettings,
                ServiceSettings = @this.ServiceSettings,
                DrivePreferenceOrder = new List<string>(@this.DrivePreferenceOrder)
            };

            var filteredCaches = new Dictionary<string, NamedCacheSettings>(@this.CacheSettingsByCacheName.Comparer);

            foreach (KeyValuePair<string, NamedCacheSettings> kvp
                in @this.CacheSettingsByCacheName)
            {
                // check that the stamp has the capabilities required by the named cache.
                if (kvp.Value.RequiredCapabilities != null && kvp.Value.RequiredCapabilities.Count > 0)
                {
                    string missingCaps = String.Join(",", kvp.Value.RequiredCapabilities
                        .Where(cap => !hostCapabilities.Contains(cap, StringComparer.OrdinalIgnoreCase)));
                    if (!String.IsNullOrEmpty(missingCaps))
                    {
                        logger.Debug(
                            "Named cache '{0}' was discarded since environment lacks required capabilities: {1}.",
                            kvp.Key, missingCaps);

                        continue;
                    }
                }

                // check that machine the drive required by named cache.
                // TODO: Should remove this, after measuring this doesn't happens.  If the drive layout doesn't match capability 
                //       we'd rather fail.
                AbsolutePath rootPath = GetCacheRootPathWithScenario(@this, kvp.Key);
                string root = Path.GetPathRoot(rootPath.Path);

                if (!checkDriveExists(root))
                {
                    logger.Error(
                        "Named cache '{0}' was discarded since the drive required by {1} does not exist or is inaccessible on the machine.",
                        kvp.Key, rootPath);

                    continue;
                }

                filteredCaches.Add(kvp.Key, kvp.Value);
            }

            result.CacheSettingsByCacheName = filteredCaches;
            result.DrivePreferenceOrder = GetSupportedDrivePreferenceOrder(@this.DrivePreferenceOrder, filteredCaches, logger);

            return result;
        }

        /// <summary>
        /// Variant of <see cref="FilterUnsupportedNamedCaches"/> which does not log. 
        /// For us in UTs and ConfigCop, where the logging is not plumbed anywhere. (And ommiting the type simplify assembly dependencies)
        /// </summary>
        /// <param name="this"></param>
        /// <param name="hostCapabilities"></param>
        /// <param name="driveExistenceOverride"></param>
        /// <returns></returns>
        public static LocalCasSettings FilterUnsupportedNamedCachesNoLogging(
            this LocalCasSettings @this, IEnumerable<string> hostCapabilities, Predicate<string> driveExistenceOverride = null)
        {
            return FilterUnsupportedNamedCaches(
                @this, hostCapabilities, BuildXL.Cache.ContentStore.Logging.NullLogger.Instance, driveExistenceOverride);
        }

        public static AbsolutePath GetCacheRootPathWithScenario(
            this LocalCasSettings @this, string cacheName)
        {
            return new AbsolutePath(
                @this.GetCacheRootPath(cacheName, @this.ServiceSettings.ScenarioName ?? DefaultScenario));
        }

        private static List<string> GetSupportedDrivePreferenceOrder(
            IEnumerable<string> drivePreferenceOrder, IReadOnlyDictionary<string, NamedCacheSettings> namedCaches, ILogger logger)
        {
            if (drivePreferenceOrder == null)
            {
                return null;
            }
            var finalPreferenceOrder = new List<string>();

            foreach (string drive in drivePreferenceOrder)
            {
                if (namedCaches.Any(kvp =>
                     kvp.Value.CacheRootPath.StartsWith(drive, StringComparison.OrdinalIgnoreCase)))
                {
                    finalPreferenceOrder.Add(drive);
                }
                else
                {
                    logger.Debug(
                        "Drive '{0}' in preference order was discarded, because its not supported by any named cache",
                        drive);
                }
            }

            return finalPreferenceOrder;
        }
    }
}
