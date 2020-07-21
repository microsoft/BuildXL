// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class MachineIdCollectionTests
    {
        [Fact]
        public void CountIsCorrect()
        {
            TestProperty((array, machineIds) => array.Length == machineIds.Count());
        }

        [Fact]
        public void CollectionsEquality()
        {
            TestProperty((array, machineIds) => Enumerable.SequenceEqual(array, machineIds));
        }

        /// <summary>
        /// A very naive version of property-based testing API.
        /// </summary>
        /// <remarks>
        /// This implementation may be moved to a proper property-based testing framework like FsCheck.
        /// </remarks>
        private static void TestProperty(Func<MachineId[], MachineIdCollection, bool> propertyChecker, [CallerMemberName]string operation = null)
        {
            const ushort maxMachineId = 2048;
            const int count = 50;
            var random = new Random();

            // Explicitly validating the property for empty collection and the collection with 1 element.
            validateProperty(0);
            validateProperty(1);

            for (int i = 0; i < count; i++)
            {
                var collectionLength = random.Next(count);
                validateProperty(collectionLength);
            }

            void validateProperty(int collectionLength)
            {
                var arrayOfMachineIds = Enumerable.Range(1, collectionLength).Select(_ => new MachineId(random.Next(maxMachineId))).ToArray();

                var machineIdCollection = arrayOfMachineIds.Length == 1 ? MachineIdCollection.Create(arrayOfMachineIds[0]) : MachineIdCollection.Create(arrayOfMachineIds);
                Assert.True(propertyChecker(arrayOfMachineIds, machineIdCollection), $"The property '{operation}' is falsifiable.");
            }
        }
    }
}
