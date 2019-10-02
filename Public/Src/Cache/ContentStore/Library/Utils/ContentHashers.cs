// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Global instance that contains all <see cref="IContentHasher"/> instances.
    /// </summary>
    /// <remarks>
    /// Each and every content hasher instance contains an expensive set of pooled resources used through the entire application's lifetime.
    /// To avoid duplication and excessive memory usage, this type provides a global entry point for accessing hasher instances.
    /// </remarks>
    public static class ContentHashers
    {
        private static readonly Dictionary<HashType, IContentHasher> Hashers = HashInfoLookup.CreateAll();

        /// <summary>
        /// Returns an instance of <see cref="IContentHasher"/> for a given <paramref name="hashType"/>.
        /// </summary>
        /// <remarks>
        /// The method throws <see cref="InvalidOperationException"/> if a given hash type is not registered in a global map.
        /// </remarks>
        public static IContentHasher Get(HashType hashType)
        {
            if (Hashers.TryGetValue(hashType, out IContentHasher hasher))
            {
                return hasher;
            }

            throw new InvalidOperationException($"The hasher type '{hashType}' is unknown.");
        }
    }
}
