// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using BuildXL.Engine.Cache.Serialization;
using System.Collections.Generic;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.Engine.Cache
{
    public class ChangeListTests
    {
        public ChangeListTests()
        {
        }

        private struct TestPair
        {
            public List<int> OldList;

            public List<int> NewList;
        }

        [Fact]
        public void Tests()
        {
            var testPairs = new TestPair[]
            {
                new TestPair
                {
                    OldList = new List<int> { 6, 2, 4, 0 },
                    NewList = new List<int> { 0, 2, 3, 4, 1 }
                },
                new TestPair
                {
                    OldList = new List<int> { 1, 2, 3, 4, 1 },
                    NewList = new List<int> { 3, 4, 1, 2, 1, 3 }
                },
                new TestPair
                {
                    OldList = new List<int> { 9, 8, 7 },
                    NewList = new List<int> { 9, 1, 2, 6 }
                },
                new TestPair
                {
                    OldList = new List<int> { 3, 9, 8, 3, 9, 7, 9, 7, 0 },
                    NewList = new List<int> { 3, 3, 9, 9, 9, 1, 7, 2, 0, 6 }
                },
                new TestPair
                {
                    OldList = new List<int> { 1, 2 },
                    NewList = new List<int> { 3, 4, 5 }
                }
            };

            foreach (var test in testPairs)
            {
                var changeList = new ChangeList<int>(test.OldList, test.NewList);
                AssertValidChange(test.OldList, test.NewList, changeList);
            }
        }

        [Fact]
        public void NoChangeTests()
        {
            var emptyList = new List<int>();
            var changeList = new ChangeList<int>(emptyList, emptyList);
            XAssert.AreEqual(0, changeList.Count);

            var list = new List<int> { 6, 2, 4, 0 };
            changeList = new ChangeList<int>(list, list);
            XAssert.AreEqual(0, changeList.Count);
        }

        /// <summary>
        /// Checks that when the changes in change list are applied to
        /// old list, the resulting list has the same values as new list.
        /// </summary>
        private void AssertValidChange<T>(List<T> oldList, List<T> newList, ChangeList<T> changeList)
        {
            var transformedOld = ListToDictionary<T>(oldList);

            for (int i = 0; i < changeList.Count; ++i)
            {
                var change = changeList[i];
                switch (change.ChangeType)
                {
                    case ChangeList<T>.ChangeType.Removed:
                        if (transformedOld[change.Value] == 1)
                        {
                            transformedOld.Remove(change.Value);
                        }
                        else
                        {
                            transformedOld[change.Value]--;
                        }
                        break;
                    case ChangeList<T>.ChangeType.Added:
                        if (transformedOld.ContainsKey(change.Value))
                        {
                            transformedOld[change.Value]++;
                        }
                        else
                        {
                            transformedOld[change.Value] = 1;
                        }
                        break;
                }
            }

            var transformedNew = ListToDictionary(newList);
            XAssert.AreEqual(transformedOld.Count, transformedNew.Count);

            foreach (var valueCount in transformedNew)
            {
                XAssert.AreEqual(valueCount.Value, transformedOld[valueCount.Key]);
            }
        }

        /// <summary>
        /// Transforms a list to a dictionary with a count of the number of times
        /// each value appears.
        /// </summary>
        private Dictionary<T, int> ListToDictionary<T>(List<T> list)
        {
            var listDictionary = new Dictionary<T, int>();
            foreach (var value in list)
            {
                if (listDictionary.ContainsKey(value))
                {
                    listDictionary[value]++;
                }
                else
                {
                    listDictionary[value] = 1;
                }
            }

            return listDictionary;
        }
    }
}
