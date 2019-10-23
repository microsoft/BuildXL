// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Extension methods for HashType
    /// </summary>
    public static class HashTypeExtensions
    {
        private static readonly Dictionary<string, HashType> NameToValue =
            new Dictionary<string, HashType>(StringComparer.OrdinalIgnoreCase)
            {
                {"MD5", HashType.MD5},
                {"SHA1", HashType.SHA1},
                {"SHA256", HashType.SHA256},
                {"VSO0", HashType.Vso0},
                {"DEDUPCHUNK", HashType.DedupChunk},
                {"DEDUPNODE", HashType.DedupNode},
                {"DEDUPNODEORCHUNK", HashType.DedupNodeOrChunk},
            };

        private static readonly Dictionary<HashType, string> ValueToName = NameToValue.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        /// <summary>
        ///     Lookup a hash type by case-insensitive name string.
        /// </summary>
        public static HashType FindHashTypeByName(this string name)
        {
            if (!Enum.TryParse(name, true, out HashType hashType))
            {
                throw new ArgumentException($"HashType by name={name} is not recognized.");
            }

            return hashType;
        }

        /// <summary>
        ///     Serialize to string.
        /// </summary>
        public static string Serialize(this HashType hashType)
        {
            if (ValueToName.TryGetValue(hashType, out var result))
            {
                return result;
            }

            return hashType.ToString().ToUpperInvariant();
        }

        /// <summary>
        ///     Deserialize from string.
        /// </summary>
        public static bool Deserialize(this string value, out HashType hashType)
        {
            Contract.Requires(value != null);

            return NameToValue.TryGetValue(value, out hashType);
        }
    }
}
