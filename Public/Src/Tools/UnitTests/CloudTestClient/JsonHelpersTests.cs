// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tool.CloudTestClient;
using Xunit;

namespace Test.Tool.CloudTestClient
{
    public class JsonHelpersTests
    {
        #region ReadJsonFile

        [Fact]
        public void ReadJsonFileReturnsNullForNullPath()
        {
            Assert.Null(JsonHelpers.ReadJsonFile<object>(null));
        }

        [Fact]
        public void ReadJsonFileReturnsNullForEmptyPath()
        {
            Assert.Null(JsonHelpers.ReadJsonFile<object>(string.Empty));
        }

        [Fact]
        public void ReadJsonFileThrowsForMissingFile()
        {
            Assert.Throws<InvalidOperationException>(() =>
                JsonHelpers.ReadJsonFile<object>(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".json")));
        }

        [Fact]
        public void ReadJsonFileDeserializesCorrectly()
        {
            using var temp = new TempDirectory();
            string path = temp.WriteFile("data.json", """{"name":"test","value":42}""");
            var result = JsonHelpers.ReadJsonFile<SimpleRecord>(path);
            Assert.Equal("test", result.Name);
            Assert.Equal(42, result.Value);
        }

        private record SimpleRecord(string Name, int Value);

        #endregion

        #region ReadJsonDocument

        [Fact]
        public void ReadJsonDocumentReturnsNullForNullPath()
        {
            Assert.Null(JsonHelpers.ReadJsonDocument(null));
        }

        [Fact]
        public void ReadJsonDocumentParsesFile()
        {
            using var temp = new TempDirectory();
            string path = temp.WriteFile("doc.json", """{"key":"val"}""");
            using var doc = JsonHelpers.ReadJsonDocument(path);
            Assert.Equal("val", doc.RootElement.GetProperty("key").GetString());
        }

        [Fact]
        public void ReadJsonDocumentThrowsForMissingFile()
        {
            Assert.Throws<InvalidOperationException>(() =>
                JsonHelpers.ReadJsonDocument(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".json")));
        }

        #endregion

        #region CloudTestPathConverter

        [Theory]
        [InlineData("""{"path":"scripts/setup.ps1"}""", @"[WorkingDirectory]\scripts/setup.ps1", "relative path gets prefixed")]
        [InlineData("""{"path":{"prefix":"BuildRoot","path":"bin/test.dll"}}""", @"[BuildRoot]\bin/test.dll", "prefixed path resolved")]
        [InlineData("""{"path":{"prefix":"WorkingDirectory"}}""", "[WorkingDirectory]", "prefix-only path")]
        [InlineData("""{"path":{"prefix":"VSODrop","path":"TestFiles/group.xml"}}""", @"[VSODrop]\TestFiles/group.xml", "VSODrop prefixed path")]
        [InlineData("""{"path":{"prefix":"LoggingDirectory","path":"results"}}""", @"[LoggingDirectory]\results", "LoggingDirectory prefixed path")]
        public void CloudTestPathConverterDeserializes(string json, string expected, string scenario)
        {
            _ = scenario;
            var result = DeserializePath(json);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CloudTestPathConverterAbsolutePathPassesThrough()
        {
            // Use a path that is absolute on the current OS
            string absolutePath = Path.Combine(Path.GetTempPath(), "drop", "test.exe");
            string json = $"{{\"path\":\"{JsonEncodedText.Encode(absolutePath)}\"}}";
            var result = DeserializePath(json);
            Assert.Equal(absolutePath, result);
        }

        [Fact]
        public void CloudTestPathConverterPrefixedPathMissingPrefixThrows()
        {
            Assert.Throws<JsonException>(() => DeserializePath("""{"path":{"notprefix":"X"}}"""));
        }

        private static string DeserializePath(string json)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var record = JsonSerializer.Deserialize<PathHolder>(json, options);
            return record.Path;
        }

        private record PathHolder([property: JsonConverter(typeof(JsonHelpers.CloudTestPathConverter))] string Path);

        #endregion

        #region ScriptArgsConverter

        [Fact]
        public void ScriptArgsConverterStringPassesThrough()
        {
            var result = DeserializeArgs("""{"args":"--flag value"}""");
            Assert.Equal("--flag value", result);
        }

        [Fact]
        public void ScriptArgsConverterNumberConverted()
        {
            var result = DeserializeArgs("""{"args":42}""");
            Assert.Equal("42", result);
        }

        [Fact]
        public void ScriptArgsConverterNullReturnsNull()
        {
            var result = DeserializeArgs("""{"args":null}""");
            Assert.Null(result);
        }

        [Fact]
        public void ScriptArgsConverterCompoundWithSeparator()
        {
            var result = DeserializeArgs("""{"args":{"values":["a.dll","b.dll","c.dll"],"separator":","}}""");
            Assert.Equal("a.dll,b.dll,c.dll", result);
        }

        [Fact]
        public void ScriptArgsConverterCompoundDefaultSeparator()
        {
            var result = DeserializeArgs("""{"args":{"values":["--config","test.json","--timeout","30"]}}""");
            Assert.Equal("--config test.json --timeout 30", result);
        }

        [Fact]
        public void ScriptArgsConverterNestedCompound()
        {
            // Outer: space-separated, inner: comma-separated
            var result = DeserializeArgs("""{"args":{"values":["-Refs",{"values":["a.dll","b.dll"],"separator":","}],"separator":" "}}""");
            Assert.Equal("-Refs a.dll,b.dll", result);
        }

        [Fact]
        public void ScriptArgsConverterCompoundWithNumbers()
        {
            var result = DeserializeArgs("""{"args":{"values":["--timeout",60],"separator":" "}}""");
            Assert.Equal("--timeout 60", result);
        }

        [Fact]
        public void ScriptArgsConverterCompoundMissingValuesThrows()
        {
            Assert.Throws<JsonException>(() => DeserializeArgs("""{"args":{"separator":","}}"""));
        }

        private static string DeserializeArgs(string json)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var record = JsonSerializer.Deserialize<ArgsHolder>(json, options);
            return record.Args;
        }

        private record ArgsHolder([property: JsonConverter(typeof(JsonHelpers.ScriptArgsConverter))] string Args);

        #endregion
    }
}
