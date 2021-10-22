// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedTable;
using System;
using System.Diagnostics;
using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    public class PackedTableNameIndexTests : TemporaryStorageTestBase
    {
        public PackedTableNameIndexTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void NameIndex_contains_all_names()
        {
            StringTable stringTable = new StringTable();
            StringTable.CachingBuilder stringTableBuilder = new StringTable.CachingBuilder(stringTable);

            NameTable nameTable = new NameTable('.', stringTable);
            NameTable.Builder nameTableBuilder = new NameTable.Builder(nameTable, stringTableBuilder);

            NameId id = nameTableBuilder.GetOrAdd("a.b.c");
            NameId id2 = nameTableBuilder.GetOrAdd("a.b.d.e");
            NameId id3 = nameTableBuilder.GetOrAdd("a.f.g.h");

            NameIndex nameIndex = new NameIndex(nameTable);

            XAssert.AreEqual(8, nameIndex.Count);
            XAssert.AreEqual(3, nameIndex[id].Length);
            XAssert.AreEqual(4, nameIndex[id2].Length);
            XAssert.AreEqual(4, nameIndex[id3].Length);

            // We know these are the string IDs because string IDs get added as the names are constructed,
            // and we happened to add names with each successive atom in lexical order.
            StringId a = new StringId(1);
            StringId b = new StringId(2);
            StringId c = new StringId(3);
            StringId d = new StringId(4);
            StringId e = new StringId(5);
            StringId f = new StringId(6);
            StringId g = new StringId(7);
            StringId h = new StringId(8);

            XAssert.AreArraysEqual(new[] { a, b, c }, nameIndex.Enumerate(id).Select(entry => entry.Atom).ToArray(), true);
            XAssert.AreArraysEqual(new[] { a, b, d, e }, nameIndex.Enumerate(id2).Select(entry => entry.Atom).ToArray(), true);
            XAssert.AreArraysEqual(new[] { a, f, g, h }, nameIndex.Enumerate(id3).Select(entry => entry.Atom).ToArray(), true);
        }
    }
}
