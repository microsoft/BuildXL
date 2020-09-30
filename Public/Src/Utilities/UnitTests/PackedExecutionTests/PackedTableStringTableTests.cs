// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedTable;
using System;
using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    public class PackedTableStringTableTests : TemporaryStorageTestBase
    {
        public PackedTableStringTableTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void StringTable_can_store_one_element()
        {
            StringTable stringTable = new StringTable();
            StringTable.CachingBuilder builder = new StringTable.CachingBuilder(stringTable);

            StringId id = builder.GetOrAdd("a");
            StringId id2 = builder.GetOrAdd("a");
            XAssert.IsTrue(id.Equals(id2));
            XAssert.IsFalse(id.Equals(default));
            XAssert.AreEqual("a", new string(stringTable[id]));
            XAssert.AreEqual(1, stringTable.Count);
            XAssert.AreEqual(1, stringTable.Ids.Count());
        }

        [Fact]
        public void StringTable_can_store_two_elements()
        {
            StringTable stringTable = new StringTable();
            StringTable.CachingBuilder builder = new StringTable.CachingBuilder(stringTable);

            StringId id = builder.GetOrAdd("a");
            StringId id2 = builder.GetOrAdd("b");
            XAssert.IsFalse(id.Equals(id2));
            XAssert.AreEqual("b", new string(stringTable[id2]));
            XAssert.AreEqual(2, stringTable.Count);
            XAssert.AreEqual(2, stringTable.Ids.Count());
        }

        [Fact]
        public void StringTable_can_save_and_load()
        {
            StringTable stringTable = new StringTable();
            StringTable.CachingBuilder builder = new StringTable.CachingBuilder(stringTable);

            builder.GetOrAdd("a");
            builder.GetOrAdd("b");

            stringTable.SaveToFile(TemporaryDirectory, $"{nameof(StringTable)}.bin");

            StringTable stringTable2 = new StringTable();
            stringTable2.LoadFromFile(TemporaryDirectory, $"{nameof(StringTable)}.bin");

            XAssert.AreEqual(stringTable.Count, stringTable2.Count);
            foreach (StringId id in stringTable.Ids)
            {
                XAssert.AreEqual(new string(stringTable[id]), new string(stringTable2[id]));
            }
        }
    }
}
