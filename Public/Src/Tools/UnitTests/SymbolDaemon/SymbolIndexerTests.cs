// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Tool.SymbolDaemon;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.SymbolDaemon
{
    public sealed class SymbolIndexerTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        internal ITestOutputHelper Output { get; }

        public SymbolIndexerTests(ITestOutputHelper output)
            : base(output)
        {
            Output = output;
        }

        [Theory]
        [InlineData(null, "{}")]
        [InlineData(null, "[]")]
        [InlineData(null, "{'version': 3, 'sources': ['source1', 'source2'], 'names': []}")]
        [InlineData(null, "INVALIDJSON")]
        [InlineData(null, "{'version': 3, 'x_microsoft_symbol_client_key': 'missingFile', 'sources': ['source1', 'source2'], 'names': []}")]
        [InlineData("file.js/myguid/file.js", "{'version': 3, 'x_microsoft_symbol_client_key': 'myguid', 'file': 'file.js', 'sources': ['source1', 'source2'], 'names': []}")]
        [InlineData("file.js/myguid/file.js", "{'version': 3, 'file': 'file.js', 'x_microsoft_symbol_client_key': 'myguid', 'sources': ['source1', 'source2'], 'names': [], END OF THIS FILE HAS ERRORS AND SHOULD PASS SINCE WE DO NOT PARSE THEM")]
        [InlineData(null, "{'x_microsoft_symbol_client_key': 'myguidNoFile'}")]
        [InlineData("file.js/3/file.js", "{'x_microsoft_symbol_client_key': 3, 'file': 'file.js'}")]
        public void TryGetSymbolClientKeyFromJsMapTest(string expectedKey, string fileContents)
        {
            var file = Path.Combine(TestOutputDirectory, Guid.NewGuid().ToString());
            File.WriteAllText(file, fileContents);
            var clientKey = SymbolIndexer.TryGetSymbolClientKeyFromJsMap(file);
            Assert.Equal(expectedKey, clientKey);
            File.Delete(file);
        }

        [Fact]
        public void MissingFile()
        {
            var file = Path.Combine(TestOutputDirectory, Guid.NewGuid().ToString());
            var clientKey = SymbolIndexer.TryGetSymbolClientKeyFromJsMap(file);
            Assert.Equal(null, clientKey);
        }

        [Fact]
        public void LockedFile()
        {
            var file = Path.Combine(TestOutputDirectory, Guid.NewGuid().ToString());
            using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var clientKey = SymbolIndexer.TryGetSymbolClientKeyFromJsMap(file);
                Assert.Equal(null, clientKey);
            }
        }

        [Theory]
        [InlineData("{}", false, null, "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a")]
        [InlineData("{'x_microsoft_symbol_client_key': 'myguid'}", false, null, "1219e776f01c240ce990b72e6b98fd7736c0ebc85b83af7b3d8508becb5334fe")]
        [InlineData("{'x_microsoft_symbol_client_key': 'myguid'}", true, "Blob:FB17794EE382BA69F00BC6ACB5F2D221F166A9270E7E1C3C798E98BF854B778500", "1219e776f01c240ce990b72e6b98fd7736c0ebc85b83af7b3d8508becb5334fe")]
        [InlineData("{'x_microsoft_symbol_client_key': 'myguid', 'file': 'file.js'}", false, null, "myguid")]
        [InlineData("{'x_microsoft_symbol_client_key': 'myguid', 'file': 'file.js'}", true, "Blob:953B1F8A32BB1AB854D8EEE3E5783C3B3EE4DF952570A8FB5D1F4A90B6A83C4F00", "myguid")]
        public async Task GetJsMapDebugEntryAsync(string fileContents, bool calculateBlobId, string expectedBlobId, string expectedClientKey)
        {
            var file = Path.Combine(TestOutputDirectory, Guid.NewGuid().ToString());
            File.WriteAllText(file, fileContents);
            var entries = await SymbolIndexer.GetJsMapDebugEntryAsync(new FileInfo(file), calculateBlobId);
            Assert.Equal(1, entries.Length);
            var entry = entries[0];
            Assert.Equal(expectedBlobId, entry.BlobIdentifier?.ToString());

            var clientKeyParts = entry.ClientKey.Split('/');
            Assert.Equal(3, clientKeyParts.Length);
            Assert.Equal(expectedClientKey, clientKeyParts[1]);
            File.Delete(file);
        }
    }
}
