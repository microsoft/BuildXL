// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Some extension methods that are useful when analyzing a cache
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Enumerates input weak fingerprints and returns all strong fingerprints
        /// </summary>
        /// <param name="weakFingerprints">Weak fingerprints to iterate over</param>
        /// <param name="readOnlySession">The read only session to use to
        /// enumerate strong fingerprints</param>
        /// <returns>All strong fingerprints enumerated from input weak fingerprints</returns>
        public static IEnumerable<StrongFingerprint> ProduceStrongFingerprints(this IEnumerable<WeakFingerprintHash> weakFingerprints, ICacheReadOnlySession readOnlySession)
        {
            HashSet<StrongFingerprint> strongFingerprintsAlreadyProduced = new HashSet<StrongFingerprint>();
            foreach (WeakFingerprintHash weakFingerprint in weakFingerprints)
            {
                IEnumerable<Task<Possible<StrongFingerprint, Failure>>> possibleStrongFingerprintTasks = readOnlySession.EnumerateStrongFingerprints(weakFingerprint);
                foreach (Task<Possible<StrongFingerprint, Failure>> possibleStrongFingerprintTask in possibleStrongFingerprintTasks.OutOfOrderTasks())
                {
                    Possible<StrongFingerprint, Failure> possibleStrongFingerprint = possibleStrongFingerprintTask.Result;
                    if (possibleStrongFingerprint.Succeeded && strongFingerprintsAlreadyProduced.Add(possibleStrongFingerprint.Result))
                    {
                        yield return possibleStrongFingerprint.Result;
                    }
                }
            }
        }

        /// <summary>
        /// Iterates over input strong fingerprints and returns all unique weak fingerprints
        /// </summary>
        /// <param name="strongFingerprints">Strong fingerprints to iterate over</param>
        /// <returns>All unique weak fingerprints found among input strong fingerprints</returns>
        public static IEnumerable<WeakFingerprintHash> ProduceWeakFingerprints(this IEnumerable<StrongFingerprint> strongFingerprints)
        {
            HashSet<WeakFingerprintHash> weakFingerprintsAlreadyFound = new HashSet<WeakFingerprintHash>();
            foreach (StrongFingerprint strongFingerprint in strongFingerprints)
            {
                WeakFingerprintHash weakFingerprint = strongFingerprint.WeakFingerprint;
                if (weakFingerprintsAlreadyFound.Add(weakFingerprint))
                {
                    yield return weakFingerprint;
                }
            }
        }

        /// <summary>
        /// This method allows us to maintain the pipeline pattern while also
        /// being able to create a set of all weak fingerprints seen so that a
        /// file of them can be generated, if desired.
        /// </summary>
        /// <param name="weakFingerprints">Weak fingerprints to add to set</param>
        /// <param name="weakFingerprintsFound">A set to which every weak fingerprint is added</param>
        /// <returns>The same weak fingerprints in the same order as the weakFingerprints input</returns>
        public static IEnumerable<WeakFingerprintHash> AddWeakFingerprintsToSet(this IEnumerable<WeakFingerprintHash> weakFingerprints, ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound)
        {
            foreach (WeakFingerprintHash weakFingerprint in weakFingerprints)
            {
                weakFingerprintsFound.TryAdd(weakFingerprint, 0);
                yield return weakFingerprint;
            }
        }

        /// <summary>
        /// Helper method that enumerates into tasks when everything we have is local
        /// </summary>
        /// <typeparam name="T">Type of object for items</typeparam>
        /// <param name="items">Items to enumerate into tasks</param>
        /// <returns>Enumerable of tasks of items</returns>
        public static IEnumerable<Task<T>> EnumerateIntoTasks<T>(this IEnumerable<T> items)
        {
            foreach (T item in items)
            {
                yield return Task.FromResult(item);
            }
        }

        internal static async Task<Tuple<CasHash, long>> GetContentSizeAsync(this ICacheReadOnlySession session, CasHash casHash)
        {
            var possibleString = await session.PinToCasAsync(casHash);
            if (!possibleString.Succeeded)
            {
                return new Tuple<CasHash, long>(casHash, (long)ContentError.UnableToPin);
            }

            var possibleStream = await session.GetStreamAsync(casHash);
            if (!possibleStream.Succeeded)
            {
                return new Tuple<CasHash, long>(casHash, (long)ContentError.UnableToStream);
            }

            long length = possibleStream.Result.Length;
#pragma warning disable AsyncFixer02
            possibleStream.Result.Dispose();
#pragma warning restore AsyncFixer02

            return new Tuple<CasHash, long>(casHash, length);
        }

        internal static IEnumerable<Task<Possible<CasEntries, Failure>>> GetCacheEntries(this ICacheReadOnlySession session, IEnumerable<StrongFingerprint> strongFingerprints)
        {
            foreach (var strongFingerprint in strongFingerprints)
            {
                yield return session.GetCacheEntryAsync(strongFingerprint);
            }
        }

        internal static IEnumerable<Task<Tuple<CasHash, long>>> GetContentSizes(this ICacheReadOnlySession session, IEnumerable<CasHash> casEntries)
        {
            foreach (var casHash in casEntries)
            {
                yield return session.GetContentSizeAsync(casHash);
            }
        }

        /// <summary>
        /// This extension method produces a collection of percentiles
        /// and cumulative distribution at percentile boundaries for a given
        /// set of long values.
        /// </summary>
        /// <param name="set">Supplies an <![CDATA[IEnumerable<long>]]> of values.</param>
        /// <param name="percentiles">Supplies an array of integer percentile values in the range 1-99.</param>
        /// <returns>
        /// Returns a SortedDictionary mapping percentiles to tuples of {percentile value, cumulative value}.
        /// The dictionary will always include an entry for p100 (max)
        /// </returns>
        internal static SortedDictionary<int, Tuple<long, long>> GetPercentilesAndCdg(this IEnumerable<long> set, IEnumerable<int> percentiles)
        {
            var result = new SortedDictionary<int, Tuple<long, long>>();
            var sortedList = set.ToArray();

            Array.Sort(sortedList);

            long accumulator = 0;
            int previousIndex = 0;

            foreach (var p in percentiles.OrderBy(i => i))
            {
                if (p < 1 || p > 99)
                {
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Invalid percentile specified: {0}", p));
                }

                int exclusive = (sortedList.Length * p) / 100; // TODO: this should interpolate...
                accumulator += sortedList.Skip(previousIndex).Take(exclusive - previousIndex).Sum();
                result.Add(p, new Tuple<long, long>(sortedList[exclusive], accumulator));
                previousIndex = exclusive;
            }

            // Always provide the min/max
            accumulator += sortedList.Skip(previousIndex).Take(sortedList.Length - previousIndex).Sum();

            result.Add(0, new Tuple<long, long>(sortedList[0], 0));
            result.Add(100, new Tuple<long, long>(sortedList[sortedList.Length - 1], accumulator));

            return result;
        }
    }
}
