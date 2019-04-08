// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class BloomFilterTests
    {
        [Fact]
        public void OptimalParameterCalculation()
        {
            BloomFilter.Parameters parameters = BloomFilter.Parameters.CreateOptimalWithFalsePositiveProbability(1000, 0.05);
            XAssert.AreEqual(4, parameters.NumberOfHashFunctions);
            XAssert.AreEqual(6236, parameters.NumberOfBits);

            parameters = BloomFilter.Parameters.CreateOptimalWithFalsePositiveProbability(10000, 0.001);
            XAssert.AreEqual(10, parameters.NumberOfHashFunctions);
            XAssert.AreEqual(143776, parameters.NumberOfBits);
        }

        [Fact]
        public void EmptyFilterDoesNotHaveFalsePositives()
        {
            var filter = new BloomFilter(new BloomFilter.Parameters(numberOfBits: 256, numberOfHashFunctions: 3));
            Item[] items = GenerateUniqueRandomItems(new Random(123), count: 10);
            foreach (Item i in items)
            {
                XAssert.IsFalse(filter.PossiblyContains(i.GetHash()));
            }
        }

        [Fact]
        public void AddedItemsAreNotFalseNegatives()
        {
            const int NumElements = 50;
            var filter = new BloomFilter(BloomFilter.Parameters.CreateOptimalWithFalsePositiveProbability(NumElements, 0.05));

            Item[] items = AddUniqueRandomItems(filter, new Random(123), NumElements);
            foreach (Item i in items)
            {
                XAssert.IsTrue(filter.PossiblyContains(i.GetHash()));
            }
        }

        [Fact]
        public void TestFalsePositiveRateSmall()
        {
            VerifyFalsePositiveRate(numberOfItems: 100, targetFalsePositiveRate: 0.05, randomSeed: 2020);
        }

        [Fact]
        public void TestFalsePositiveRateLarge()
        {
            VerifyFalsePositiveRate(numberOfItems: 1000, targetFalsePositiveRate: 0.02, randomSeed: 2021);
        }

        [Fact]
        public void TestFalsePositiveRateLarge2()
        {
            VerifyFalsePositiveRate(numberOfItems: 3037, targetFalsePositiveRate: 0.03, randomSeed: 1929);
        }

        private static void VerifyFalsePositiveRate(int numberOfItems, double targetFalsePositiveRate, int randomSeed)
        {
            const int NumberOfIntentionallyAbsentItems = 10000;
            const double AllowedErrorMagnitude = 0.2;

            var filter = new BloomFilter(BloomFilter.Parameters.CreateOptimalWithFalsePositiveProbability(numberOfItems, targetFalsePositiveRate));

            Item[] allItems = GenerateUniqueRandomItems(new Random(randomSeed), numberOfItems + NumberOfIntentionallyAbsentItems);

            for (int i = 0; i < numberOfItems; i++)
            {
                filter.Add(allItems[i].GetHash());
            }

            int numberOfFalsePositives = 0;
            for (int i = numberOfItems; i < allItems.Length; i++)
            {
                if (filter.PossiblyContains(allItems[i].GetHash()))
                {
                    numberOfFalsePositives++;
                }
            }

            double expectedFalsePositives = targetFalsePositiveRate * NumberOfIntentionallyAbsentItems;
            double error = (expectedFalsePositives - numberOfFalsePositives) / expectedFalsePositives;

            XAssert.IsTrue(
                Math.Abs(error) <= AllowedErrorMagnitude,
                "The false-positive rate was signficantly different than theoretical. This should be unlikely. Actual: {0} Expected: {1} Error ratio: {2} (allowed {3})",
                numberOfFalsePositives,
                expectedFalsePositives,
                error,
                AllowedErrorMagnitude);
        }

        private static Item[] AddUniqueRandomItems(BloomFilter filter, Random random, int count)
        {
            Item[] items = GenerateUniqueRandomItems(random, count);
            foreach (Item item in items)
            {
                filter.Add(item.GetHash());
            }

            return items;
        }

        private static Item[] GenerateUniqueRandomItems(Random random, int count)
        {
            var results = new Item[count];
            var newItems = new HashSet<Item>();
            for (int i = 0; i < count;)
            {
                Item item = Item.CreateRandom(random);
                if (newItems.Add(item))
                {
                    results[i] = item;
                    i++;
                }
            }

            return results;
        }

        private struct Item : IEquatable<Item>
        {
            public ulong A;
            public ulong B;
            public ulong C;

            public static Item CreateRandom(Random random)
            {
                Item item;
                item.A = GetRandomLong(random);
                item.B = GetRandomLong(random);
                item.C = GetRandomLong(random);
                return item;
            }

            private static ulong GetRandomLong(Random random)
            {
                ulong high = (ulong)random.Next();
                ulong low = (ulong)random.Next();
                return (high << 32) | low;
            }

            public MurmurHash3 GetHash()
            {
                unsafe
                {
                    Item i = this;
                    Item* p = &i;
                    return MurmurHash3.Create((byte*)p, (uint)sizeof(Item), 0);
                }
            }

            public bool Equals(Item other)
            {
                return A == other.A && B == other.B && C == other.C;
            }

            public override bool Equals(object obj)
            {
                return StructUtilities.Equals(this, obj);
            }

            public override int GetHashCode()
            {
                return unchecked((int)A);
            }
        }
    }
}
