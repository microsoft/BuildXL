// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes
{
    public class FileAccessAllowlistTests
    {
        [Fact]
        public async Task TestSerialization()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            var pathTable = context.PathTable;
            var stringTable = context.StringTable;
            var symbolTable = new SymbolTable(pathTable.StringTable);
            var allowlist = new FileAccessAllowlist(context);

            //Allowlist with full paths
            var path1 = new DiscriminatingUnion<AbsolutePath, PathAtom>(AbsolutePath.Create(pathTable, @"\\fakePath\foo.txt"));
            var path2 = new DiscriminatingUnion<AbsolutePath, PathAtom>(AbsolutePath.Create(pathTable, @"\\fakePath\bar.txt"));
            var regex1 = new SerializableRegex(@"dir\foo.txt");
            var executableEntry1 = new ExecutablePathAllowlistEntry(
                path1, regex1, true, "entry1");
            var executableEntry2 = new ExecutablePathAllowlistEntry(
                path2, new SerializableRegex("bar"), false, "entry2");
            allowlist.Add(executableEntry1);
            allowlist.Add(executableEntry2);

            // Allowlist with executable names only
            var path3 = new DiscriminatingUnion<AbsolutePath, PathAtom>(PathAtom.Create(stringTable, "alice.txt"));
            var path4 = new DiscriminatingUnion<AbsolutePath, PathAtom>(PathAtom.Create(stringTable, "bob.txt"));
            var regex3 = new SerializableRegex(@"dir\alice.txt");
            var executableEntry3 = new ExecutablePathAllowlistEntry(
                path3, regex3, true, "entry5");
            var executableEntry4 = new ExecutablePathAllowlistEntry(
                path4, new SerializableRegex("bob"), false, "entry6");
            allowlist.Add(executableEntry3);
            allowlist.Add(executableEntry4);

            var symbol1 = FullSymbol.Create(symbolTable, "symbol1");
            var valueEntry = new ValuePathFileAccessAllowlistEntry(
                symbol1, new SerializableRegex("symbol1"), false, null);

            var symbol2 = FullSymbol.Create(symbolTable, "symbol2");
            var valueEntry2 = new ValuePathFileAccessAllowlistEntry(
                symbol2, new SerializableRegex("symbol2"), false, "entry4");
            allowlist.Add(valueEntry);
            allowlist.Add(valueEntry2);

            XAssert.AreEqual(4, allowlist.UncacheableEntryCount);
            XAssert.AreEqual(2, allowlist.CacheableEntryCount);
            XAssert.AreEqual("Unnamed", valueEntry.Name);

            using (var ms = new MemoryStream())
            {
                BuildXLWriter writer = new BuildXLWriter(true, ms, true, true);
                allowlist.Serialize(writer);

                ms.Position = 0;
                BuildXLReader reader = new BuildXLReader(true, ms, true);
                var deserialized = await FileAccessAllowlist.DeserializeAsync(reader, Task.FromResult<PipExecutionContext>(context));
                var path1Absolute = (AbsolutePath)path1.GetValue();
                var path2Absolute = (AbsolutePath)path2.GetValue();
                var path3Atom = ((PathAtom)path3.GetValue()).StringId;
                var path4Atom = ((PathAtom)path4.GetValue()).StringId;

                XAssert.AreEqual(2, deserialized.ExecutablePathEntries.Count);
                XAssert.AreEqual(1, deserialized.ExecutablePathEntries[path1Absolute].Count);
                XAssert.AreEqual(true, deserialized.ExecutablePathEntries[path1Absolute][0].AllowsCaching);
                XAssert.AreEqual(regex1.ToString(), deserialized.ExecutablePathEntries[path1Absolute][0].PathRegex.ToString());
                XAssert.AreEqual(executableEntry1.Name, deserialized.ExecutablePathEntries[path1Absolute][0].Name);
                XAssert.AreEqual(executableEntry2.Name, deserialized.ExecutablePathEntries[path2Absolute][0].Name);

                XAssert.AreEqual(2, deserialized.ToolExecutableNameEntries.Count);
                XAssert.AreEqual(1, deserialized.ToolExecutableNameEntries[path3Atom].Count);
                XAssert.AreEqual(true, deserialized.ToolExecutableNameEntries[path3Atom][0].AllowsCaching);
                XAssert.AreEqual(regex3.ToString(), deserialized.ToolExecutableNameEntries[path3Atom][0].PathRegex.ToString());
                XAssert.AreEqual(executableEntry3.Name, deserialized.ToolExecutableNameEntries[path3Atom][0].Name);
                XAssert.AreEqual(executableEntry4.Name, deserialized.ToolExecutableNameEntries[path4Atom][0].Name);

                XAssert.AreEqual(2, deserialized.ValuePathEntries.Count);
                XAssert.AreEqual(1, deserialized.ValuePathEntries[symbol1].Count);
                XAssert.AreEqual(false, deserialized.ValuePathEntries[symbol1][0].AllowsCaching);
                XAssert.AreEqual(valueEntry.Name, deserialized.ValuePathEntries[symbol1][0].Name);
                XAssert.AreEqual(valueEntry2.Name, deserialized.ValuePathEntries[symbol2][0].Name);

                XAssert.AreEqual(4, deserialized.UncacheableEntryCount);
                XAssert.AreEqual(2, deserialized.CacheableEntryCount);
            }
        }
    }
}
