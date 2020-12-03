// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// A helper class that tracks the hash range visited during reconciliation events processing.
    /// </summary>
    public class UpdatedHashesVisitor
    {
        /// <nodoc />
        public ShortHash? AddLocationsMinHash { get; private set; }
        /// <nodoc />
        public ShortHash? AddLocationsMaxHash { get; private set; }

        /// <nodoc />
        public ShortHash? RemoveLocationsMinHash { get; private set; }
        /// <nodoc />
        public ShortHash? RemoveLocationsMaxHash { get; private set; }

        /// <nodoc />
        public void AddLocationsHashProcessed(ShortHash hash)
        {
            AddLocationsMinHash = Min(AddLocationsMinHash, hash);
            AddLocationsMaxHash = Max(AddLocationsMaxHash, hash);
        }

        /// <nodoc />
        public void AddLocationsHashProcessed(IReadOnlyList<ShortHash> hashes)
        {
            foreach (var hash in hashes.AsStructEnumerable())
            {
                AddLocationsHashProcessed(hash);
            }
        }

        /// <nodoc />
        public void RemoveLocationsHashProcessed(ShortHash hash)
        {
            RemoveLocationsMinHash = Min(RemoveLocationsMinHash, hash);
            RemoveLocationsMaxHash = Max(RemoveLocationsMaxHash, hash);
        }

        /// <nodoc />
        public void RemoveLocationsHashProcessed(IReadOnlyList<ShortHash> hashes)
        {
            foreach (var hash in hashes.AsStructEnumerable())
            {
                RemoveLocationsHashProcessed(hash);
            }
        }

        private static ShortHash Min(ShortHash? current, ShortHash candidate)
        {
            if (current == null)
            {
                return candidate;
            }

            return current.Value < candidate ? current.Value : candidate;
        }

        private static ShortHash Max(ShortHash? current, ShortHash candidate)
        {
            if (current == null)
            {
                return candidate;
            }

            return current.Value > candidate ? current.Value : candidate;
        }
    }
}
