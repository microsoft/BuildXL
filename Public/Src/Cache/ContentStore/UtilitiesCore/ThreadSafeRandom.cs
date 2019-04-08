// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Cache.ContentStore.UtilitiesCore
{
    /// <summary>
    ///     Provides a thread-safe source of randomness
    /// </summary>
    public static class ThreadSafeRandom
    {
        /// <summary>
        ///     Each thread gets its own Random
        /// </summary>
        private static readonly ThreadLocal<Random> TlsRand =
            new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref _rndSeed)));

        /// <summary>
        ///     Seed that all threads increment so that many threads starting in parallel still get different different results
        /// </summary>
        private static int _rndSeed = Environment.TickCount;

        /// <summary>
        ///     Gets thread-safe randomness generator
        /// </summary>
        public static Random Generator => TlsRand.Value;

        /// <nodoc />
        public static void SetSeed(int seed)
        {
            TlsRand.Value = new Random(seed);
        }

        /// <summary>
        ///     Construct an array of random bytes in a thread-safe manner.
        /// </summary>
        /// <param name="count">Number of bytes to generate</param>
        /// <returns>The array of random bytes.</returns>
        public static byte[] GetBytes(int count)
        {
            Contract.Requires(count >= 0);
            var bytes = new byte[count];
            Generator.NextBytes(bytes);
            return bytes;
        }

        /// <summary>
        ///     Shuffle array in place.
        /// </summary>
        /// <param name="array">Array to be shuffled</param>
        public static void Shuffle<T>(IList<T> array)
        {
            int n = array.Count;
            while (n > 1)
            {
                int k = Generator.Next(n--);
                T temp = array[n];
                array[n] = array[k];
                array[k] = temp;
            }
        }
    }
}
