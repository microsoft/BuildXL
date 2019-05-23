// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

[module: System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses",
    Scope = "type",
    Target = "BuildXL.Cache.InputListFilter.InputListFilterCacheFactory+Config",
    Justification = "Tool is confused - it is constructed generically")]

namespace BuildXL.Cache.InputListFilter
{
    /// <summary>
    /// A cache factory for a cache that can do some filtering of observed inputs to prevent
    /// certain fingerprints from being added to the cache
    /// </summary>
    /// <remarks>
    /// This cache is mainly to help hunt down a problem we are seeing currently but the overall
    /// design allows for some extra, build configuration controlled, rules to be applied to items
    /// that can be cached based on observed inputs.
    /// </remarks>
    public sealed class InputListFilterCacheFactory : ICacheFactory
    {
        // CompositingCacheFactory JSON CONFIG DATA
        // {
        //     "Assembly":"BuildXL.Cache.Compositing",
        //     "Type":"BuildXL.Cache.InMemory.CompositingCacheFactory",
        //     "FilteredCache":{0}
        //     "MustInclude":{1}
        //     "MustNotInclude":{2}
        // }
        private sealed class Config
        {
            /// <summary>
            /// The Metadata Cache configuration
            /// </summary>
            public ICacheConfigData FilteredCache { get; set; }

            /// <summary>
            /// The regex that must match at least one line
            /// in the input list otherwise this is a bad entry
            /// </summary>
            /// <remarks>
            /// Empty string means it is not executed
            ///
            /// Note that each file in the observed inputs will be run
            /// against this regex until one file matches the regex.
            /// This is an atleast one match, not an all must match.
            ///
            /// The failure includes the full strong fingerprint that
            /// would have been added to the cache had the filter not
            /// blocked it.
            /// </remarks>
            [DefaultValue("")]
            public string MustInclude { get; set; }

            /// <summary>
            /// The regex that must not match any line
            /// in the input list otherwise this is a bad entry
            /// </summary>
            /// <remarks>
            /// Empty string means it is not executed
            ///
            /// Note that all files will be run against this regex
            /// until the first match, at which point a failure is
            /// returned.  It does return in the failure the path/file
            /// that was the cause but it does not return all of the
            /// files/paths that were the cause.
            ///
            /// The failure includes the full strong fingerprint that
            /// would have been added to the cache had the filter not
            /// blocked it.
            /// </remarks>
            [DefaultValue("")]
            public string MustNotInclude { get; set; }
        }

        /// <inheritdoc />
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(ICacheConfigData cacheData, Guid activityId)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<Config>();
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            Config config = possibleCacheConfig.Result;

            Regex mustIncludeRegex = null;
            if (!string.IsNullOrWhiteSpace(config.MustInclude))
            {
                try
                {
                    mustIncludeRegex = new Regex(config.MustInclude, RegexOptions.Compiled);
                }
                catch (Exception e)
                {
                    return new RegexFailure(config.MustInclude, e);
                }
            }

            Regex mustNotIncludeRegex = null;
            if (!string.IsNullOrWhiteSpace(config.MustNotInclude))
            {
                try
                {
                    mustNotIncludeRegex = new Regex(config.MustNotInclude, RegexOptions.Compiled);
                }
                catch (Exception e)
                {
                    return new RegexFailure(config.MustNotInclude, e);
                }
            }

            var maybeCache = await CacheFactory.InitializeCacheAsync(config.FilteredCache, activityId);
            if (!maybeCache.Succeeded)
            {
                return maybeCache.Failure;
            }

            ICache cache = maybeCache.Result;

            try
            {
                return new InputListFilterCache(cache, mustIncludeRegex, mustNotIncludeRegex);
            }
            catch
            {
                Analysis.IgnoreResult(await cache.ShutdownAsync(), justification: "Okay to ignore shutdown");
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<Config>(cacheData, config =>
            {
                var failures = new List<Failure>();
                if (!string.IsNullOrWhiteSpace(config.MustInclude))
                {
                    try
                    {
                        var mustIncludeRegex = new Regex(config.MustInclude, RegexOptions.Compiled);
                    }
                    catch (Exception e)
                    {
                        failures.Add(new RegexFailure(config.MustInclude, e));
                    }
                }

                if (!string.IsNullOrWhiteSpace(config.MustNotInclude))
                {
                    try
                    {
                        var mustNotIncludeRegex = new Regex(config.MustNotInclude, RegexOptions.Compiled);
                    }
                    catch (Exception e)
                    {
                        failures.Add(new RegexFailure(config.MustNotInclude, e));
                    }
                }

                failures.AddRange(
                    CacheFactory.ValidateConfig(config.FilteredCache)
                        .Select(failure => new Failure<string>($"{nameof(config.FilteredCache)} validation failed.", failure)));

                return failures;
            });
        }
    }
}
