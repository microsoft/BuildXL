// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Analyzer.Analyzers;
using BuildXL.Utilities;
using Xunit;

namespace Test.Tool.DScript.Analyzer
{
    public class PathFixerTests
        : AnalyzerTest<PathFixerAnalyzer>
    {
        private static readonly string PathPrefix = OperatingSystemHelper.IsUnixOS ? "/TestModule/" : "C:\\TestModule\\";

        [Fact]
        public void FixAllPathsUnixKeepCasing()
        {
            TestSuccess(
                AllCases,
@"const f1 = f`FiLe.TxT`;
const f2 = f`fOlDeR/FiLe.TxT`;
const f3 = f`fOlDeR/FiLe.TxT`;
const f4 = f`/fOlDeR/FiLe.TxT`;
const f5 = f`/fOlDeR/fOlDeR/FiLe.TxT`;
const d1 = d`FoLdEr`;
const d2 = d`fOlDeR/FoLdEr`;
const d3 = d`fOlDeR/FoLdEr`;
const d4 = d`/fOlDeR/FoLdEr`;
const d5 = d`/fOlDeR/fOlDeR/FoLdEr`;
const p1 = p`FiLe.TxT`;
const p2 = p`fOlDeR/FiLe.TxT`;
const p3 = p`fOlDeR/FiLe.TxT`;
const p4 = p`/fOlDeR/FiLe.TxT`;
const p5 = p`/fOlDeR/fOlDeR/FiLe.TxT`;
const r1 = r`FiLe.TxT`;
const r2 = r`fOlDeR/FiLe.TxT`;
const r3 = r`fOlDeR/fOlDeR/FiLe.TxT`;",
                options: new[] { "/p:Unix" });
        }

        [Fact]
        public void FixAllPathsWindowsKeepCasing()
        {
            TestSuccess(
                AllCases,
@"const f1 = f`FiLe.TxT`;
const f2 = f`fOlDeR\FiLe.TxT`;
const f3 = f`fOlDeR\FiLe.TxT`;
const f4 = f`\fOlDeR\FiLe.TxT`;
const f5 = f`\fOlDeR\fOlDeR\FiLe.TxT`;
const d1 = d`FoLdEr`;
const d2 = d`fOlDeR\FoLdEr`;
const d3 = d`fOlDeR\FoLdEr`;
const d4 = d`\fOlDeR\FoLdEr`;
const d5 = d`\fOlDeR\fOlDeR\FoLdEr`;
const p1 = p`FiLe.TxT`;
const p2 = p`fOlDeR\FiLe.TxT`;
const p3 = p`fOlDeR\FiLe.TxT`;
const p4 = p`\fOlDeR\FiLe.TxT`;
const p5 = p`\fOlDeR\fOlDeR\FiLe.TxT`;
const r1 = r`FiLe.TxT`;
const r2 = r`fOlDeR\FiLe.TxT`;
const r3 = r`fOlDeR\fOlDeR\FiLe.TxT`;",
                options: new[] { "/p:Windows" });
        }

        [Fact]
        public void FixAllPathsWindowsLowerCasing()
        {
            TestSuccess(
                AllCases,
@"const f1 = f`FiLe.TxT`;
const f2 = f`folder\FiLe.TxT`;
const f3 = f`folder\FiLe.TxT`;
const f4 = f`\folder\FiLe.TxT`;
const f5 = f`\folder\folder\FiLe.TxT`;
const d1 = d`folder`;
const d2 = d`folder\folder`;
const d3 = d`folder\folder`;
const d4 = d`\folder\folder`;
const d5 = d`\folder\folder\folder`;
const p1 = p`FiLe.TxT`;
const p2 = p`folder\FiLe.TxT`;
const p3 = p`folder\FiLe.TxT`;
const p4 = p`\folder\FiLe.TxT`;
const p5 = p`\folder\folder\FiLe.TxT`;
const r1 = r`FiLe.TxT`;
const r2 = r`folder\FiLe.TxT`;
const r3 = r`folder\folder\FiLe.TxT`;",
                options: new[] { "/p:Windows", "/l" });
        }

        [Fact]
        public void PassUnixSlashWithoutFix()
        {
            TestSuccess(
                @"const f1 = f`folder/file.txt`;",
                fix: false,
                options: new[] { "/p:Unix" });
        }

        [Fact]
        public void PassWindowsSlashWithoutFix()
        {
            TestSuccess(
                @"const f1 = f`folder\file.txt`;",
                fix: false,
                options: new[] { "/p:Windows" });
        }

        [Fact]
        public void FailWindowsSlashInUnixFile()
        {
            TestErrorReport(
                @"const f1 = f`folder\file.txt`;",
                $"{PathPrefix}0.dsc(1,19): Use path separator '/' rather than '\\' in 'folder\\file.txt'",
                options: new[] { "/p:Unix" });
        }

        [Fact]
        public void FailUnixSlashInWindowsFile()
        {
            TestErrorReport(
                @"const f1 = f`folder/file.txt`;",
                $"{PathPrefix}0.dsc(1,19): Use path separator '\\' rather than '/' in 'folder/file.txt'",
                options: new[] { "/p:Windows" });
        }

        [Fact]
        public void FailCaseFile()
        {
            TestErrorReport(
                @"const f1 = f`fOlder/file.txt`;",
                $"{PathPrefix}0.dsc(1,14): Use lowercase for all directory parts. Use 'folder' rather than 'fOlder' in 'fOlder/file.txt'.",
                options: new[] { "/l" });
        }

        [Fact]
        public void FailCasePath()
        {
            TestErrorReport(
                @"const f1 = p`fOlder/file.txt`;",
                $"{PathPrefix}0.dsc(1,14): Use lowercase for all directory parts. Use 'folder' rather than 'fOlder' in 'fOlder/file.txt'.",
                options: new[] { "/l" });
        }

        [Fact]
        public void FailCaseRelativePath()
        {
            TestErrorReport(
                @"const f1 = r`fOlder/file.txt`;",
                $"{PathPrefix}0.dsc(1,14): Use lowercase for all directory parts. Use 'folder' rather than 'fOlder' in 'fOlder/file.txt'.",
                options: new[] { "/l" });
        }

        [Fact]
        public void FailCaseDirectory()
        {
            TestErrorReport(
                @"const f1 = d`folder/fOlder`;",
                $"{PathPrefix}0.dsc(1,21): Use lowercase for all directory parts. Use 'folder' rather than 'fOlder' in 'folder/fOlder'.",
                options: new[] { "/l" });
        }

        private const string AllCases = @"
const f1 = f`FiLe.TxT`;
const f2 = f`fOlDeR\FiLe.TxT`;
const f3 = f`fOlDeR/FiLe.TxT`;
const f4 = f`/fOlDeR\FiLe.TxT`;
const f5 = f`\fOlDeR/fOlDeR/FiLe.TxT`;
const d1 = d`FoLdEr`;
const d2 = d`fOlDeR\FoLdEr`;
const d3 = d`fOlDeR/FoLdEr`;
const d4 = d`/fOlDeR\FoLdEr`;
const d5 = d`\fOlDeR/fOlDeR/FoLdEr`;
const p1 = p`FiLe.TxT`;
const p2 = p`fOlDeR\FiLe.TxT`;
const p3 = p`fOlDeR/FiLe.TxT`;
const p4 = p`/fOlDeR\FiLe.TxT`;
const p5 = p`\fOlDeR/fOlDeR/FiLe.TxT`;
const r1 = r`FiLe.TxT`;
const r2 = r`fOlDeR\FiLe.TxT`;
const r3 = r`fOlDeR/fOlDeR/FiLe.TxT`;
";
    }
}
