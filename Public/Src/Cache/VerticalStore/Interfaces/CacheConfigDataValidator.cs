// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.Interfaces
{
    /// <nodoc />
    public static class CacheConfigDataValidator
    {
        /// <nodoc />
        public static IEnumerable<Failure> ValidateConfiguration<T>(ICacheConfigData cacheData, Func<T, IEnumerable<Failure>> validate) where T : class, new()
        {
            var possibleCacheConfig = cacheData.Create<T>();
            if (!possibleCacheConfig.Succeeded)
            {
                return new[] { possibleCacheConfig.Failure };
            }

            return validate(possibleCacheConfig.Result);
        }
    }
}
