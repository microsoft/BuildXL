// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Engine.Cache
{
    /// <summary>
    /// Tests for the <see cref="KeyValueStoreAccessor"/> class that and the underlying
    /// <see cref="IBuildXLKeyValueStore"/> implementation.
    /// </summary>
    public class KeyValueStoreTests : TemporaryStorageTestBase
    {
        private string StoreDirectory { get; set; }

        public KeyValueStoreTests()
        {
            StoreDirectory = Path.Combine(TemporaryDirectory, "store");
        }

        [Fact]
        public void FailToCreateStoreIfDirectoryInUse()
        {
            using (var firstStore = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                var secondStore = KeyValueStoreAccessor.Open(StoreDirectory);
                XAssert.IsFalse(secondStore.Succeeded);
            }
        }

        [Fact]
        public void PutGetKey()
        {
            string key1 = "key1", value1 = "value1";
            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1);
                    AssertEntryExists(store, key1, value1);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1);
                }).Succeeded);
            }
        }

        [Fact]
        public void PutMultipleKeys()
        {
            string key1 = "key1", value1A = "value1A";
            string key2 = "key2", value2A = "value2A";
            string key3 = "key3", value3A = "value3A";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1A);
                    store.Put(key2, value2A);
                    store.Put(key3, value3A);

                    AssertEntryExists(store, key1, value1A);
                    AssertEntryExists(store, key2, value2A);
                    AssertEntryExists(store, key3, value3A);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1A);
                    AssertEntryExists(store, key2, value2A);
                    AssertEntryExists(store, key3, value3A);
                }).Succeeded);
            }
        }

        [Fact]
        public void PutMultipleKeysMultipleSessions()
        {
            string key1 = "key1", value1A = "value1A";
            string key2 = "key2", value2A = "value2A";
            string key3 = "key3", value3A = "value3A";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1A);
                    AssertEntryExists(store, key1, value1A);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key2, value2A);

                    AssertEntryExists(store, key1, value1A);
                    AssertEntryExists(store, key2, value2A);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key3, value3A);

                    AssertEntryExists(store, key1, value1A);
                    AssertEntryExists(store, key2, value2A);
                    AssertEntryExists(store, key3, value3A);
                }).Succeeded);
            }
        }

        [Fact]
        public void PutNullValue()
        {
            string key1 = null, value1 = "value1";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    Exception exception = null;
                    try
                    {
                        store.Put(key1, value1);
                    }
                    catch (Exception e)
                    {
                        exception = e;
                    }

                    XAssert.AreNotEqual(null, exception);
                }).Succeeded);
            }
        }

        [Fact]
        public void PutNullKey()
        {
            string key1 = null, value1 = "value1";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    Exception exception = null;
                    try
                    {
                        store.Put(key1, value1);
                    }
                    catch (Exception e)
                    {
                        exception = e;
                    }

                    XAssert.AreNotEqual(null, exception);
                }).Succeeded);
            }
        }

        [Fact]
        public void RemoveNullKey()
        {
            string key1 = null;

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    Exception exception = null;
                    try
                    {
                        store.Remove(key1);
                    }
                    catch (Exception e)
                    {
                        exception = e;
                    }

                    XAssert.AreNotEqual(null, exception);
                }).Succeeded);
            }
        }


        [Fact]
        public void GetNullKey()
        {
            string key1 = null;

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    Exception exception = null;
                    try
                    {
                        store.TryGetValue(key1, out var value);
                    }
                    catch (Exception e)
                    {
                        exception = e;
                    }

                    XAssert.AreNotEqual(null, exception);
                }).Succeeded);
            }
        }

        [Fact]
        public void GetNonExistentKey()
        {
            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryAbsent(store, "key");

                    // Check that a key not found failure does not cause 
                    // store to become disabled
                    store.Put("key", "value");
                }).Succeeded);
            }
        }

        [Fact]
        public void RemoveKey()
        {
            string key1 = "key1", value1 = "value1";
            string key2 = "key2", value2 = "value2";
            string key3 = "key3", value3 = "value3";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1);
                    store.Put(key2, value2);
                    store.Put(key3, value3);

                    AssertEntryExists(store, key1, value1);
                    AssertEntryExists(store, key2, value2);
                    AssertEntryExists(store, key3, value3);

                    store.Remove(key1);
                    store.Remove(key2);
                    store.Remove(key3);

                    AssertEntryAbsent(store, key1);
                    AssertEntryAbsent(store, key2);
                    AssertEntryAbsent(store, key3);
                }).Succeeded);
            }
        }


        [Fact]
        public void RemoveBatch()
        {
            string key1 = "key1", value1 = "value1";
            string key2 = "key2", value2 = "value2";
            string key3 = "key3", value3 = "value3";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1);
                    store.Put(key2, value2);
                    store.Put(key3, value3);

                    AssertEntryExists(store, key1, value1);
                    AssertEntryExists(store, key2, value2);
                    AssertEntryExists(store, key3, value3);

                    store.RemoveBatch(new string[]
                    {
                        key1,
                        key2,
                        key3
                    });
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryAbsent(store, key1);
                    AssertEntryAbsent(store, key2);
                    AssertEntryAbsent(store, key3);
                }).Succeeded);
            }
        }

        [Fact]
        public void RemoveBatchMultipleColumns()
        {
            string key1 = "key1", value1 = "value1";
            string key2 = "key2", value2 = "value2";
            string key3 = "key3", value3 = "value3";

            string column2 = "column2", column3 = "column3";
            var additionalColumns = new string[]
            {
                    column2
            };

            var additionalKeyTrackedColumns = new string[]
            {
                    column3
            };

            var columnNames = new string[]
            {
                null,
                column2,
                column3
            };

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    foreach (var column in columnNames)
                    {
                        store.Put(key1, value1, column);
                        store.Put(key2, value2, column);
                        store.Put(key3, value3, column);
                    }

                    // Remove keys
                    store.RemoveBatch(
                    new string[]
                    {
                        key1,
                        key2,
                        key3
                    },
                    new string[]
                    {
                        null,
                        column2,
                        column3,
                    });
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    foreach (var column in columnNames)
                    {
                        AssertEntryAbsent(store, key1, column);
                        AssertEntryAbsent(store, key2, column);
                        AssertEntryAbsent(store, key3, column);
                    }
                }).Succeeded);
            }
        }

        [Fact]
        public void RemoveKeyMultipleSessions()
        {
            string key1 = "key1", value1 = "value1";
            string key2 = "key2", value2 = "value2";
            string key3 = "key3", value3 = "value3";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1);
                    store.Put(key2, value2);
                    store.Put(key3, value3);

                    AssertEntryExists(store, key1, value1);
                    AssertEntryExists(store, key2, value2);
                    AssertEntryExists(store, key3, value3);

                    store.Remove(key1);
                    AssertEntryAbsent(store, key1);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryAbsent(store, key1);
                    AssertEntryExists(store, key2, value2);
                    AssertEntryExists(store, key3, value3);

                    store.Remove(key2);
                    AssertEntryAbsent(store, key2);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryAbsent(store, key1);
                    AssertEntryAbsent(store, key2);
                    AssertEntryExists(store, key3, value3);

                    store.Remove(key3);
                    AssertEntryAbsent(store, key3);
                }).Succeeded);
            }
        }

        [Fact]
        public void GarbageCollectMultipleColumnFamilies()
        {
            string key1 = "key1", value1 = "value1";
            string key2 = "key2", value2 = "value2";
            string key3 = "key3", value3 = "value3";

            string column2 = "column2", column3 = "column3";
            var additionalColumns = new string[]
            {
                column2
            };

            var additionalKeyTrackedColumns = new string[]
            {
                column3
            };

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1);
                    store.Put(key2, value2);
                    store.Put(key3, value3);

                    store.Put(key1, value1, column2);
                    store.Put(key3, value3, column2);

                    store.Put(key1, value1, column3);
                    store.Put(key3, value3, column3);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1);
                    AssertEntryExists(store, key2, value2);
                    AssertEntryExists(store, key3, value3);

                    AssertEntryExists(store, key1, value1, column2);
                    AssertEntryExists(store, key3, value3, column2);

                    AssertEntryExists(store, key1, value1, column3);
                    AssertEntryExists(store, key3, value3, column3);

                    // Garbage collect key2 and key3
                    Func<string, bool> canGarbageCollect = (s) => (s == key2 || s == key3);

                    // Garbage collect using default column's keys, but across all column families
                    var gcStats = store.GarbageCollect(canGarbageCollect, additionalColumnFamilies: new string[] { column2, column3 });
                    // Stats are for primary column
                    XAssert.AreEqual(2, gcStats.RemovedCount);
                    XAssert.AreEqual(3, gcStats.TotalCount);

                    // key3 should have already been removed by the cross-column garbage collect
                    var gcStats2 = store.GarbageCollect(canGarbageCollect, column2);
                    XAssert.AreEqual(0, gcStats2.RemovedCount);
                    XAssert.AreEqual(1, gcStats2.TotalCount);

                    // key3 should have already been removed by the cross-column garbage collect
                    var gcStats3 = store.GarbageCollect(canGarbageCollect, column3);
                    XAssert.AreEqual(0, gcStats3.RemovedCount);
                    XAssert.AreEqual(1, gcStats3.TotalCount);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1);
                    AssertEntryAbsent(store, key2);
                    AssertEntryAbsent(store, key3);

                    AssertEntryExists(store, key1, value1, column2);
                    AssertEntryAbsent(store, key3, column2);

                    AssertEntryExists(store, key1, value1, column3);
                    AssertEntryAbsent(store, key3, column3);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    // Garbage collect key2 and key3
                    Func<string, bool> canGarbageCollect = (s) => (s == key1);

                    // Garbage collect using default column's keys, but across all column families
                    var gcStats = store.GarbageCollect(canGarbageCollect, columnFamilyName: column2, additionalColumnFamilies: new string[] { null, column3 });
                    // Stats are for primary column
                    XAssert.AreEqual(1, gcStats.RemovedCount);
                    XAssert.AreEqual(1, gcStats.TotalCount);

                    // key1 should have already been removed by the cross-column garbage collect
                    var gcStats2 = store.GarbageCollect(canGarbageCollect);
                    XAssert.AreEqual(0, gcStats2.RemovedCount);
                    XAssert.AreEqual(0, gcStats2.TotalCount);

                    // key1 should have already been removed by the cross-column garbage collect
                    var gcStats3 = store.GarbageCollect(canGarbageCollect, column3);
                    XAssert.AreEqual(0, gcStats3.RemovedCount);
                    XAssert.AreEqual(0, gcStats3.TotalCount);
                }).Succeeded);
            }
        }

        [Fact]
        public void OverwriteKeySameSession()
        {
            string key1 = "key1", value1A = "value1A";
            var value1B = "value1B";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1A);
                    AssertEntryExists(store, key1, value1A);

                    store.Put(key1, value1B);
                    AssertEntryExists(store, key1, value1B);
                }).Succeeded);
            }
        }

        [Fact]
        public void OverwriteKeyMultipleSessions()
        {
            string key1 = "key1", value1A = "value1A";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1A);
                }).Succeeded);
            }

            var value1B = "value1B";
            // Overwrite a key in a new session
            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1A);

                    store.Put(key1, value1B);
                    AssertEntryExists(store, key1, value1B);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1B);
                }).Succeeded);
            }
        }

        [Fact]
        public void PutLongValue()
        {
            string key1 = "key1", value1A = LongRandomString(); // ~350k char long

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1A);
                    AssertEntryExists(store, key1, value1A);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1A);
                }).Succeeded);
            }
        }

        [Fact]
        public void TestByteArrayInterface()
        {
            byte[] key1 = StringToBytes("key1"), value1 = StringToBytes("value1");

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1);
                    AssertEntryExists(store, key1, value1);

                    store.Remove(key1);
                    AssertEntryAbsent(store, key1);
                }).Succeeded);
            }
        }

        private byte[] StringToBytes(string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }

        [Fact]
        public void MultipleColumnFamilies()
        {
            string key1 = "key1", value1 = "value1";
            string key2 = "key2", value2 = "value2";
            string key3 = "key3", value3 = "value3";

            string column2 = "column2", column3 = "column3";
            var additionalColumns = new string[]
            {
                column2,
                column3,
            };

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    // 1 default + 2 additional column families
                    XAssert.AreEqual(3, CountColumnFamilies());

                    // Puts in default column
                    store.Put(key1, value1);
                    AssertEntryExists(store, key1, value1);
                    AssertEntryAbsent(store, key1, column2);
                    AssertEntryAbsent(store, key1, column2);

                    store.Put(key2, value2, column2);
                    AssertEntryAbsent(store, key2);
                    AssertEntryExists(store, key2, value2, column2);
                    AssertEntryAbsent(store, key2, column3);

                    store.Put(key3, value3, column3);
                    AssertEntryAbsent(store, key3);
                    AssertEntryAbsent(store, key3, column2);
                    AssertEntryExists(store, key3, value3, column3);
                }).Succeeded);
            }

            // Column families persist after store is closed
            XAssert.AreEqual(3, CountColumnFamilies());
        }

        /// <summary>
        /// In addition to a column for key-value, key-tracked columns
        /// have a parallel column of just keys to prevent loading values when iterating over
        /// just keys or checking for existence.
        /// </summary>
        [Fact]
        public void MultipleKeyTrackedColumnFamilies()
        {
            string key1 = "key1", value1 = "value1";
            string key2 = "key2", value2 = "value2";
            string key3 = "key3", value3 = "value3";

            string column2 = "column2", column3 = "column3";
            var additionalKeyTrackedColumns = new string[]
            {
                column2,
                column3,
            };

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    // 1 default + 4 additional column families (2 per key tracked column)
                    XAssert.AreEqual(5, CountColumnFamilies());

                    // Puts in default column
                    store.Put(key1, value1);
                    AssertEntryExists(store, key1, value1);
                    AssertEntryAbsent(store, key1, column2);
                    AssertEntryAbsent(store, key1, column2);

                    var x = KeyValueStoreAccessor.ListColumnFamilies(StoreDirectory);
                    store.Put(key2, value2, column2);
                    AssertEntryAbsent(store, key2);
                    AssertEntryExists(store, key2, value2, column2);
                    AssertEntryAbsent(store, key2, column3);

                    store.Put(key3, value3, column3);
                    AssertEntryAbsent(store, key3);
                    AssertEntryAbsent(store, key3, column2);
                    AssertEntryExists(store, key3, value3, column3);
                }).Succeeded);
            }

            // Column families persist after store is closed
            XAssert.AreEqual(5, CountColumnFamilies());
        }

        [Fact]
        public void GarbageCollectStringsMultipleColumnFamilies()
        {
            string key1 = "key1", value1 = "value1";
            string key2 = "key2", value2 = "value2";
            string key3 = "key3", value3 = "value3";

            string column2 = "column2", column3 = "column3";
            var additionalColumns = new string[]
            {
                column2
            };

            var additionalKeyTrackedColumns = new string[]
            {
                column3
            };

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1);
                    store.Put(key2, value2);
                    store.Put(key3, value3);

                    store.Put(key1, value1, column2);
                    store.Put(key3, value3, column2);

                    store.Put(key1, value1, column3);
                    store.Put(key3, value3, column3);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1);
                    AssertEntryExists(store, key2, value2);
                    AssertEntryExists(store, key3, value3);

                    AssertEntryExists(store, key1, value1, column2);
                    AssertEntryExists(store, key3, value3, column2);

                    AssertEntryExists(store, key1, value1, column3);
                    AssertEntryExists(store, key3, value3, column3);

                    // Garbage collect key2 and key3
                    Func<string, bool> canGarbageCollect = (s) => (s == key2 || s == key3);

                    var gcStats = store.GarbageCollect(canGarbageCollect);
                    XAssert.AreEqual(2, gcStats.RemovedCount);
                    XAssert.AreEqual(3, gcStats.TotalCount);

                    var gcStats2 = store.GarbageCollect(canGarbageCollect, column2);
                    XAssert.AreEqual(1, gcStats2.RemovedCount);
                    XAssert.AreEqual(2, gcStats2.TotalCount);

                    var gcStats3 = store.GarbageCollect(canGarbageCollect, column3);
                    XAssert.AreEqual(1, gcStats3.RemovedCount);
                    XAssert.AreEqual(2, gcStats3.TotalCount);
                }).Succeeded);

            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, additionalColumns: additionalColumns, additionalKeyTrackedColumns: additionalKeyTrackedColumns).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1);
                    AssertEntryAbsent(store, key2);
                    AssertEntryAbsent(store, key3);

                    AssertEntryExists(store, key1, value1, column2);
                    AssertEntryAbsent(store, key3, column2);

                    AssertEntryExists(store, key1, value1, column3);
                    AssertEntryAbsent(store, key3, column3);
                }).Succeeded);
            }
        }

        [Fact]
        public void GarbageCollectBytes()
        {
            byte[] key1 = StringToBytes("key1"), value1 = StringToBytes("value1");
            byte[] key2 = StringToBytes("key2"), value2 = StringToBytes("value2");
            byte[] key3 = StringToBytes("key3"), value3 = StringToBytes("value3");

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    store.Put(key1, value1);
                    store.Put(key2, value2);
                    store.Put(key3, value3);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1);
                    AssertEntryExists(store, key2, value2);
                    AssertEntryExists(store, key3, value3);

                    // Garbage collect key2 and key3
                    Func<byte[], bool> canGarbageCollect = (b) => (b.SequenceEqual(key2) || b.SequenceEqual(key3));
                    var gcStats = store.GarbageCollect(canGarbageCollect);
                    XAssert.AreEqual(2, gcStats.RemovedCount);
                    XAssert.AreEqual(3, gcStats.TotalCount);
                }).Succeeded);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                XAssert.IsTrue(accessor.Use(store =>
                {
                    AssertEntryExists(store, key1, value1);
                    AssertEntryAbsent(store, key2);
                    AssertEntryAbsent(store, key3);
                }).Succeeded);
            }
        }

        [Fact]
        public void DefaultColumnKeyTracked()
        {
            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, defaultColumnKeyTracked: true).Result)
            {
                // 1 default + 1 key column
                XAssert.AreEqual(2, CountColumnFamilies());
            }
        }

        [Fact]
        public void NonExistentColumnTest()
        {
            string key1 = "key1", value1A = "value1A";

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                Assert.Throws<KeyNotFoundException>(
                    () =>
                    {
                        AssertFailed(
                            accessor.Use(
                                store =>
                                {
                                    // Accessing a non-existent column causes a failure
                                    store.Put(key1, value1A, "fakeColumn");
                                }));
                    });
            }
        }

        [Fact(Skip = "1374242")]
        public void ChangingColumnFamilies()
        {
            var additionalColumns = new HashSet<string>
            {
                "0",
                "1",
            };

            var additionalKeyTrackedColumns = new HashSet<string>
            {
                "2",
                "3",
            };

            var key = "key";
            var value = "value";
            using (var accessor = KeyValueStoreAccessor.Open(
                StoreDirectory,
                additionalColumns: additionalColumns,
                additionalKeyTrackedColumns: additionalKeyTrackedColumns,
                openReadOnly: false).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        foreach (var column in additionalColumns.Concat(additionalKeyTrackedColumns))
                        {
                            store.Put(key, value, columnFamilyName: column);
                        }
                    })
                );
            }

            // Add a column
            var changingColumn = "change";
            var changingKeyColumn = "changeKey";
            additionalColumns.Add(changingColumn);
            additionalKeyTrackedColumns.Add(changingKeyColumn);
            // Check read-write mode works
            using (var accessor = KeyValueStoreAccessor.Open(
                StoreDirectory,
                additionalColumns: additionalColumns,
                additionalKeyTrackedColumns: additionalKeyTrackedColumns,
                openReadOnly: false).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        store.Put(key, value, columnFamilyName: changingColumn);
                        store.Put(key, value, columnFamilyName: changingKeyColumn);
                    })
                );
            }

            // Check read-only mode works
            using (var accessor = KeyValueStoreAccessor.Open(
                StoreDirectory,
                additionalColumns: additionalColumns,
                additionalKeyTrackedColumns: additionalKeyTrackedColumns,
                openReadOnly: true).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        foreach (var column in additionalColumns.Concat(additionalKeyTrackedColumns))
                        {
                            AssertEntryExists(store, key, value, column: column);
                        }
                    })
                );
            }

            // Drop a column
            additionalColumns.Remove(changingColumn);
            additionalKeyTrackedColumns.Remove(changingKeyColumn);

            using (var accessor = KeyValueStoreAccessor.Open(
                StoreDirectory,
                additionalColumns: additionalColumns,
                additionalKeyTrackedColumns: additionalKeyTrackedColumns,
                openReadOnly: false,
                dropMismatchingColumns: true).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        foreach (var column in additionalColumns.Concat(additionalKeyTrackedColumns))
                        {
                            store.Put(key, value, columnFamilyName: column);
                        }

                        Exception exception = null;
                        try
                        {
                            store.TryGetValue(key, out var v, columnFamilyName: changingColumn);
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }

                        XAssert.AreNotEqual(null, exception);
                    })
                );
            }

            // Check read-only mode works
            using (var accessor = KeyValueStoreAccessor.Open(
                StoreDirectory,
                additionalColumns: additionalColumns,
                additionalKeyTrackedColumns: additionalKeyTrackedColumns,
                openReadOnly: true).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        foreach (var column in additionalColumns.Concat(additionalKeyTrackedColumns))
                        {
                            AssertEntryExists(store, key, value, column: column);
                        }

                        Exception exception = null;
                        try
                        {
                            store.TryGetValue(key, out var v, columnFamilyName: changingKeyColumn);
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }

                        XAssert.AreNotEqual(null, exception);
                    })
                );
            }
        }

        [Fact]
        public void SimultaneousReadOnlyAndWriteInstances()
        {
            string key1 = "key1", value1 = "value1", value2 = "value2";
            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        store.Put(key1, value1);
                    })
                );
            }

            // Open a read-only version of the store first, this is a snapshot of the store at a point in time
            using (var readAccessor = KeyValueStoreAccessor.Open(StoreDirectory, openReadOnly: true).Result)
            {
                // Open a simultaenous read-write version of the store
                using (var writeAccessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
                {
                    // Make modifications to the read-write version
                    AssertSuccess(
                        writeAccessor.Use(writeStore =>
                        {
                            writeStore.Put(key1, value2);
                        })
                    );

                    // Use the read-only version, which does not see any new writes
                    AssertSuccess(
                        readAccessor.Use(readStore =>
                        {
                            AssertEntryExists(readStore, key1, value1);
                        })
                    );

                    AssertSuccess(
                        writeAccessor.Use(writeStore =>
                        {
                            AssertEntryExists(writeStore, key1, value2);
                        })
                    );
                }
            }
        }

        [Fact]
        public void TestOpenWithVersioning()
        {
            string key1 = "key1", value1 = "value1";
            var version = 1;
            using (var accessor = KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        store.Put(key1, value1);
                    })
                );

                // First instance of a store should be newly created
                XAssert.IsTrue(accessor.CreatedNewStore);
            }

            using (var accessor = KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        AssertEntryExists(store, key1, value1);
                    })
                );

                // Existing stores don't need to be created
                XAssert.IsFalse(accessor.CreatedNewStore);
            }

            // Fail to open a versioned store without versioning
            XAssert.IsFalse(KeyValueStoreAccessor.Open(StoreDirectory).Succeeded);

            // Even in read only mode
            XAssert.IsFalse(KeyValueStoreAccessor.Open(StoreDirectory, openReadOnly: true).Succeeded);

            // Fail to open a store with a different version number without enabling "onFailureDeleteExistingStoreAndRetry"
            XAssert.IsFalse(KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version + 1).Succeeded);

            // Even in read only mode
            XAssert.IsFalse(KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version + 1, openReadOnly: true).Succeeded);

            // Even with onFailureDeleteExistingStoreAndRetry enabled, refuse to delete a store in read-only mode to prevent unexpected data loss
            XAssert.IsFalse(KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version + 1, openReadOnly: true, onFailureDeleteExistingStoreAndRetry: true).Succeeded);
            XAssert.IsTrue(Directory.Exists(StoreDirectory));

            // Attempts to read incompatible versions can be made by explicitly passing IgnoreStoreVersion
            using (var accessor = KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, KeyValueStoreAccessor.IgnoreStoreVersion).Result)
            {
                Analysis.IgnoreResult(
                    accessor.Use(store => { AssertEntryExists(store, key1, value1); })
                );

                XAssert.IsFalse(accessor.CreatedNewStore);
            }

            // Even in read only mode
            using (var accessor = KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, KeyValueStoreAccessor.IgnoreStoreVersion).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        AssertEntryExists(store, key1, value1);
                    })
                );

                XAssert.IsFalse(accessor.CreatedNewStore);
            }


            // Make sure the store still works
            using (var accessor = KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        AssertEntryExists(store, key1, value1);
                    })
                );

                XAssert.IsFalse(accessor.CreatedNewStore);
            }

            // Update a store to a new version number by deleting the incompatible existing store
            using (var accessor = KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version + 1, onFailureDeleteExistingStoreAndRetry: true).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        AssertEntryAbsent(store, key1);
                    })
                );

                XAssert.IsTrue(accessor.CreatedNewStore);
            }
        }

        [Fact]
        public void TestInvalidateStoreWithVersionedStore()
        {
            var version = 1;
            using (var accessor = KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        store.Put("key", "value");
                    })
                );

                XAssert.IsTrue(accessor.CreatedNewStore);
            }

            using (var accessor = KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version).Result)
            {
                AssertFailed(
                    accessor.Use(store =>
                    {
                        throw new System.Runtime.InteropServices.SEHException("Fake exception that is looked for by KeyValueStoreAccessor");
                    })
                );

                XAssert.IsFalse(accessor.CreatedNewStore);
            }

            // Fail due to invalid store version
            XAssert.IsFalse(KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version).Succeeded);

            // Make sure a new store is created due to invalid store
            using (var accessor = KeyValueStoreAccessor.OpenWithVersioning(StoreDirectory, version, onFailureDeleteExistingStoreAndRetry: true).Result)
            {
                XAssert.IsTrue(accessor.CreatedNewStore);
            }
        }

        [Fact]
        public void TestInvalidateStoreWithUnversionedStore()
        {
            // Stores that do not use the built-in key value store versioning are still subject to invalid store checks for safety reasons
            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                AssertSuccess(
                    accessor.Use(store =>
                    {
                        store.Put("key", "value");
                    })
                );

                XAssert.IsTrue(accessor.CreatedNewStore);
            }

            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory).Result)
            {
                AssertFailed(
                    accessor.Use(store =>
                    {
                        throw new System.Runtime.InteropServices.SEHException("Fake exception that is looked for by KeyValueStoreAccessor");
                    })
                );

                XAssert.IsFalse(accessor.CreatedNewStore);
            }

            // Fail due to invalid store version
            XAssert.IsFalse(KeyValueStoreAccessor.Open(StoreDirectory).Succeeded);

            // Make sure a new store is created due to invalid store
            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, onFailureDeleteExistingStoreAndRetry: true).Result)
            {
                XAssert.IsTrue(accessor.CreatedNewStore);
            }
        }

        [Fact]
        public void FailureHandlersSeeAccessViolationException()
        {
            bool failureHandled = false;
            using (var accessor = KeyValueStoreAccessor.Open(StoreDirectory, failureHandler: (f) => { failureHandled = true; }).Result)
            {
                try
                {
                    XAssert.IsFalse(accessor.Use(store =>
                    {
                        throw new AccessViolationException();
                    }).Succeeded);
                }
                catch (AccessViolationException)
                {
                    // Will be caught by failure handler
                }
            }

            XAssert.IsTrue(failureHandled);
        }

        private string LongRandomString()
        {
            // Concatenate 1000 random strings
            var b = new StringBuilder();
            for (var i = 0; i < 1000; ++i)
            {
                b.AppendFormat("{0}{1}", RandomString(), " ");
            }

            return b.ToString();
        }

        private string RandomString()
        {
            var random = new Random();
            var arraySize = random.Next(0, 1000);
            var randomBytes = new byte[arraySize];

            random.NextBytes(randomBytes);

            return Encoding.UTF8.GetString(randomBytes);
        }

        private void AssertEntryExists(IBuildXLKeyValueStore store, byte[] key, byte[] value, string column = null)
        {
            XAssert.IsTrue(store.Contains(key, column));
            var keyFound = store.TryGetValue(key, out var result, column);

            // Check key existed
            XAssert.IsTrue(keyFound);
            // Check returned result is correct
            XAssert.AreEqual(value, result);
        }

        private void AssertEntryExists(IBuildXLKeyValueStore store, string key, string value, string column = null)
        {
            XAssert.IsTrue(store.Contains(key, column));
            var keyFound = store.TryGetValue(key, out var result, column);

            // Check key existed
            XAssert.IsTrue(keyFound);
            // Check returned result is correct
            XAssert.AreEqual(value, result);
        }

        private void AssertEntryAbsent(IBuildXLKeyValueStore store, byte[] key, string column = null)
        {
            var keyFound = store.TryGetValue(key, out var result, column);

            // Check key absent
            XAssert.IsFalse(keyFound);
            // Check returned result is correct
            XAssert.AreEqual(null, result);
        }

        private void AssertEntryAbsent(IBuildXLKeyValueStore store, string key, string column = null)
        {
            var keyFound = store.TryGetValue(key, out var result, column);

            // Check key absent
            XAssert.IsFalse(keyFound);
            // Check returned result is correct
            XAssert.AreEqual(null, result);
        }

        private int CountColumnFamilies()
        {
            var count = 0;
            foreach (var column in KeyValueStoreAccessor.ListColumnFamilies(StoreDirectory))
            {
                count++;
            }
            return count;
        }

        private void AssertFailed<T>(Possible<T> possible)
        {
            Assert.False(possible.Succeeded);
        }

        private void AssertSuccess<T>(Possible<T> possible)
        {
            Assert.True(possible.Succeeded);
        }

        // Run once at the end of each test.
        protected override void Dispose(bool disposing)
        {
            try
            {
                // Delete the store on disk at the end of every test
                FileUtilities.DeleteDirectoryContents(StoreDirectory, deleteRootDirectory: true);
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }
}