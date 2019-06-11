// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Set of extension methods for <see cref="BuildXL.Cache.ContentStore.Distributed.NuCache.ContentLocationDatabase"/>
    /// </summary>
    public static class ContentLocationDatabaseExtensions
    {
        /// <summary>
        /// Enumerates all the hashes with <see cref="ContentLocationEntry"/> from a <paramref name="database"/> for a given <paramref name="currentMachineId"/>.
        /// </summary>
        public static IEnumerable<(ShortHash hash, ContentLocationEntry entry)> EnumerateSortedDatabaseEntriesForMachineId(
            this ContentLocationDatabase database,
            OperationContext context,
            MachineId currentMachineId)
        {
            foreach (var (key, entry) in database.EnumerateEntriesWithSortedKeys(
                context,
                rawValue => database.HasMachineId(rawValue, currentMachineId.Index)))
            {
                yield return (key, entry);
            }
        }

        /// <summary>
        /// Enumerates all the hashes with <see cref="ContentLocationEntry"/> from a <paramref name="database"/> for a given <paramref name="currentMachineId"/>.
        /// </summary>
        public static IEnumerable<(ShortHash hash, long size)> EnumerateSortedHashesWithContentSizeForMachineId(
            this ContentLocationDatabase database,
            OperationContext context,
            MachineId currentMachineId)
        {
            foreach (var (hash, entry) in EnumerateSortedDatabaseEntriesForMachineId(database, context, currentMachineId))
            {
                yield return (hash, entry.ContentSize);
            }
        }
    }
}
