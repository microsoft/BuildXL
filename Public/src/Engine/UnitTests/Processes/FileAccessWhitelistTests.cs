// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes
{
    public class FileAccessWhitelistTests
    {
        [Fact]
        public async Task TestSerialization()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            var pathTable = context.PathTable;
            var symbolTable = new SymbolTable(pathTable.StringTable);
            var whitelist = new FileAccessWhitelist(context);

            var path1 = AbsolutePath.Create(pathTable, @"\\fakePath\foo.txt");
            var path2 = AbsolutePath.Create(pathTable, @"\\fakePath\bar.txt");
            var regex1 = new SerializableRegex(@"dir\foo.txt");
            var executableEntry1 = new ExecutablePathWhitelistEntry(
                path1, regex1, true, "entry1");
            var executableEntry2 = new ExecutablePathWhitelistEntry(
                path2, new SerializableRegex("bar"), false, "entry2");
            whitelist.Add(executableEntry1);
            whitelist.Add(executableEntry2);

            var symbol1 = FullSymbol.Create(symbolTable, "symbol1");
            var valueEntry = new ValuePathFileAccessWhitelistEntry(
                symbol1, new SerializableRegex("symbol1"), false, null);

            var symbol2 = FullSymbol.Create(symbolTable, "symbol2");
            var valueEntry2 = new ValuePathFileAccessWhitelistEntry(
                symbol2, new SerializableRegex("symbol2"), false, "entry4");
            whitelist.Add(valueEntry);
            whitelist.Add(valueEntry2);

            XAssert.AreEqual(3, whitelist.UncacheableEntryCount);
            XAssert.AreEqual(1, whitelist.CacheableEntryCount);
            XAssert.AreEqual("Unnamed", valueEntry.Name);

            using (var ms = new MemoryStream())
            {
                BuildXLWriter writer = new BuildXLWriter(true, ms, true, true);
                whitelist.Serialize(writer);

                ms.Position = 0;
                BuildXLReader reader = new BuildXLReader(true, ms, true);
                var deserialized = await FileAccessWhitelist.DeserializeAsync(reader, Task.FromResult<PipExecutionContext>(context));

                XAssert.AreEqual(2, deserialized.ExecutablePathEntries.Count);
                XAssert.AreEqual(1, deserialized.ExecutablePathEntries[path1].Count);
                XAssert.AreEqual(true, deserialized.ExecutablePathEntries[path1][0].AllowsCaching);
                XAssert.AreEqual(regex1.ToString(), deserialized.ExecutablePathEntries[path1][0].PathRegex.ToString());
                XAssert.AreEqual(executableEntry1.Name, deserialized.ExecutablePathEntries[path1][0].Name);
                XAssert.AreEqual(executableEntry2.Name, deserialized.ExecutablePathEntries[path2][0].Name);

                XAssert.AreEqual(2, deserialized.ValuePathEntries.Count);
                XAssert.AreEqual(1, deserialized.ValuePathEntries[symbol1].Count);
                XAssert.AreEqual(false, deserialized.ValuePathEntries[symbol1][0].AllowsCaching);
                XAssert.AreEqual(valueEntry.Name, deserialized.ValuePathEntries[symbol1][0].Name);
                XAssert.AreEqual(valueEntry2.Name, deserialized.ValuePathEntries[symbol2][0].Name);

                XAssert.AreEqual(3, deserialized.UncacheableEntryCount);
                XAssert.AreEqual(1, deserialized.CacheableEntryCount);
            }
        }
    }
}
