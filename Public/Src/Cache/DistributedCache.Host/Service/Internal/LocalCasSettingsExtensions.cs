// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.Host.Service.Internal
{
    public static class LocalCasSettingsExtension
    {
        public const string DefaultScenario = "ContentAddressableStore";

        /// <summary>
        /// Filters out <see cref="NamedCacheSettings"/> entries whose capability demands are not met by the host.
        /// Also filters out entries pointing to e physical drive that doesn't exist.
        /// </summary>
        public static LocalCasSettings FilterUnsupportedNamedCaches(
            this LocalCasSettings @this, IEnumerable<string> hostCapabilities, ILogger logger)
        {
            Predicate<string> checkDriveExists = Directory.Exists;

            var result = new LocalCasSettings
            {
                CasClientSettings = @this.CasClientSettings,
                ServiceSettings = @this.ServiceSettings,
                DrivePreferenceOrder = new List<string>(@this.DrivePreferenceOrder)
            };

            var filteredCaches = new Dictionary<string, NamedCacheSettings>(@this.CacheSettingsByCacheName.Comparer);

            foreach (KeyValuePair<string, NamedCacheSettings> kvp in @this.CacheSettingsByCacheName)
            {
                // check that the stamp has the capabilities required by the named cache.
                if (kvp.Value.RequiredCapabilities != null && kvp.Value.RequiredCapabilities.Count > 0)
                {
                    string missingCaps = string.Join(",", kvp.Value.RequiredCapabilities
                        .Where(cap => !hostCapabilities.Contains(cap, StringComparer.OrdinalIgnoreCase)));
                    if (!string.IsNullOrEmpty(missingCaps))
                    {
                        logger.Debug(
                            "Named cache '{0}' was discarded since environment lacks required capabilities: {1}.",
                            kvp.Key, missingCaps);

                        continue;
                    }
                }

                AbsolutePath rootPath = @this.GetCacheRootPathWithScenario(kvp.Key);
                string root = rootPath.GetPathRoot();

                if (!checkDriveExists(root))
                {
                    // Currently it's totally fine to have, for instance, both D and K drives configured for the entire stamp,
                    // even though only some machines in the stamp have both.
                    // For instance, GlobalCache machines usually do have K drive and that drive is preferred if available,
                    // but D drive should be used for CommandAgent machines.

                    // The next trace used to be an error, but in the current state this situation is happening on all CmdAgent machines almost everywhere.
                    logger.Debug(
                        "Named cache '{0}' was discarded since the drive required by {1} does not exist or is inaccessible on the machine.",
                        kvp.Key, rootPath);

                    continue;
                }

                filteredCaches.Add(kvp.Key, kvp.Value);
            }

            result.CacheSettingsByCacheName = filteredCaches;
            result.DrivePreferenceOrder = GetSupportedDrivePreferenceOrder(@this.DrivePreferenceOrder, filteredCaches, logger);

            if (result.CacheSettingsByCacheName.Count == 0)
            {
                // It seems that all the cache configs were filtered out. This is bad and the system can't work like that!
                string message = $"All ({@this.CacheSettingsByCacheName.Count}) cache configs were discarded due to lack of capabilities. The cache service can't start without valid cache settings.";
                throw new CacheException(message);
            }

            return result;
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
