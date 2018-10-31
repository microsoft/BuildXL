// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class CollectionUtilitiesTests : XunitBuildXLTest
    {
        public CollectionUtilitiesTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void EmptyArray()
        {
            var array1 = CollectionUtilities.EmptyArray<string>();
            var array2 = CollectionUtilities.EmptyArray<string>();

            XAssert.AreEqual(0, array1.Length);
            XAssert.AreSame(array1, array2);
        }

        [Fact]
        public void AsArray()
        {
            var list = new List<int>();
            var emptyArray = CollectionUtilities.EmptyArray<int>();

            var listArray = CollectionUtilities.AsArray(list);
            XAssert.AreSame(emptyArray, listArray);

            list.Add(1);
            list.Add(2);
            list.Add(3);

            listArray = CollectionUtilities.AsArray(list);
            XAssert.IsTrue(Enumerable.SequenceEqual(list, listArray));
        }

        [Fact]
        public void DictionaryGetOrAdd()
        {
            var dictionary = new Dictionary<int, int>();

            var value = dictionary.GetOrAdd(0, k => 1);
            XAssert.AreEqual(1, value);
            XAssert.AreEqual(1, dictionary[0]);

            value = dictionary.GetOrAdd(0, k => 2);
            XAssert.AreEqual(1, value);
            XAssert.AreEqual(1, dictionary[0]);
        }
    }
}
