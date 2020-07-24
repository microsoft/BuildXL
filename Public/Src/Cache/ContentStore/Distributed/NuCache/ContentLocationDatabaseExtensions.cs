// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Set of extension methods for <see cref="ContentLocationDatabase"/>
    /// </summary>
    public static class ContentLocationDatabaseExtensions
    {
        /// <summary>
        /// Performs a compare exchange operation on metadata, while ensuring all invariants are kept. If the
        /// fingerprint is not present, then it is inserted.
        /// </summary>
        /// <returns>
        /// Result providing the call's completion status. True if the replacement was completed successfully, false otherwise.
        /// </returns>
        public static Possible<bool> CompareExchange(
            this ContentLocationDatabase database,
            OperationContext context, 
            StrongFingerprint strongFingerprint, 
            ContentHashListWithDeterminism expected,
            ContentHashListWithDeterminism replacement)
        {
            return database.TryUpsert(
                context,
                strongFingerprint,
                replacement,
                entry => entry.ContentHashListWithDeterminism.Equals(expected),
                lastAccessTimeUtc: null);
        }

        /// <summary>
        /// Enumerates all the hashes with <see cref="ContentLocationEntry"/> from a <paramref name="database"/> for a given <paramref name="currentMachineId"/>.
        /// </summary>
        private static IEnumerable<(ShortHash hash, ContentLocationEntry entry)> EnumerateSortedDatabaseEntriesForMachineId(
            this ContentLocationDatabase database,
            OperationContext context,
            MachineId currentMachineId,
            ShortHash? startingPoint)
        {
            var filter = new ContentLocationDatabase.EnumerationFilter(
                rawValue => database.HasMachineId(rawValue, currentMachineId.Index),
                startingPoint);

            foreach (var (key, entry) in database.EnumerateEntriesWithSortedKeys(context, filter))
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
            MachineId currentMachineId,
            ShortHash? startingPoint = null)
        {
            foreach (var (hash, entry) in EnumerateSortedDatabaseEntriesForMachineId(database, context, currentMachineId, startingPoint))
            {
                yield return (hash, entry.ContentSize);
            }
        }
    }
}
