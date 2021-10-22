// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedTable;
using BuildXL.Utilities.PackedExecution;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Test.Tool.Analyzers
{
    public class PackedTableNameFilterTests : TemporaryStorageTestBase
    {
        public PackedTableNameFilterTests(ITestOutputHelper output) : base(output)
        {
        }

        public static PackedExecution.Builder ConstructExecution()
        {
            PackedExecution packedExecution = new PackedExecution();
            PackedExecution.Builder builder = new PackedExecution.Builder(packedExecution);

            builder.PipTableBuilder.Add(0, "alpha.bravo.charlie", PipType.Process);
            builder.PipTableBuilder.Add(1, "alpha.bravo.delta.echo", PipType.Process);
            builder.PipTableBuilder.Add(2, "alpha.foxtrot.golf.hotel", PipType.Process);

            return builder;
        }

        [Fact]
        public void NameFilter_filters_substrings_correctly()
        {
            PackedExecution.Builder builder = ConstructExecution();

            NameIndex nameIndex = new NameIndex(builder.PackedExecution.PipTable.PipNameTable);

            NameFilter<PipId> nameFilter = new NameFilter<PipId>(
                builder.PackedExecution.PipTable,
                nameIndex,
                pid => builder.PackedExecution.PipTable[pid].Name,
                '.',
                "rav");

            PipId[] results = nameFilter.Filter().OrderBy(pid => pid).ToArray();

            XAssert.AreEqual(2, results.Count());
            XAssert.AreEqual(new PipId(1), results.First());
            XAssert.AreEqual(new PipId(2), results.Last());

            NameFilter<PipId> nameFilter2 = new NameFilter<PipId>(
                builder.PackedExecution.PipTable,
                nameIndex,
                pid => builder.PackedExecution.PipTable[pid].Name,
                '.',
                "RAV");

            PipId[] results2 = nameFilter2.Filter().OrderBy(pid => pid).ToArray();

            XAssert.AreArraysEqual(results, results2, true);
        }

        [Fact]
        public void NameFilter_filters_starts_and_ends_correctly()
        {
            PackedExecution.Builder builder = ConstructExecution();

            NameIndex nameIndex = new NameIndex(builder.PackedExecution.PipTable.PipNameTable);

            // should match "alpha.bravo" pip substrings
            NameFilter<PipId> nameFilter = new NameFilter<PipId>(
                builder.PackedExecution.PipTable,
                nameIndex,
                pid => builder.PackedExecution.PipTable[pid].Name,
                '.',
                "a.b");

            PipId[] results = nameFilter.Filter().OrderBy(pid => pid).ToArray();

            XAssert.AreEqual(2, results.Count());
            XAssert.AreEqual(new PipId(1), results.First());
            XAssert.AreEqual(new PipId(2), results.Last());
        }

        [Fact]
        public void NameFilter_filters_internal_atoms_by_equality()
        {
            PackedExecution.Builder builder = ConstructExecution();

            NameIndex nameIndex = new NameIndex(builder.PackedExecution.PipTable.PipNameTable);

            // should match "alpha.bravo" pip substrings
            NameFilter<PipId> nameFilter = new NameFilter<PipId>(
                builder.PackedExecution.PipTable,
                nameIndex,
                pid => builder.PackedExecution.PipTable[pid].Name,
                '.',
                "a.bravo.d");

            PipId[] results = nameFilter.Filter().ToArray();

            XAssert.AreEqual(1, results.Count());
            XAssert.AreEqual(new PipId(2), results.First());

            // should match "alpha.bravo" pip substrings
            NameFilter<PipId> nameFilter2 = new NameFilter<PipId>(
                builder.PackedExecution.PipTable,
                nameIndex,
                pid => builder.PackedExecution.PipTable[pid].Name,
                '.',
                "t.golf.h");

            PipId[] results2 = nameFilter2.Filter().ToArray();

            XAssert.AreEqual(1, results2.Count());
            XAssert.AreEqual(new PipId(3), results2.First());
        }
    }
}
