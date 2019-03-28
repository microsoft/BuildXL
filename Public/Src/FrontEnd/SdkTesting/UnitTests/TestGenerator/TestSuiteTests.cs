// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.FrontEnd.Script.Testing.TestGenerator;
using TypeScript.Net.Types;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    public sealed class TestSuiteTests : BaseTest
    {
        public TestSuiteTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void ParseFromDiskNormal()
        {
            var folder = GetAndCleanTestFolder(nameof(TestSuiteTests), nameof(ParseFromDiskNormal));
            var filePath = Path.Combine(folder, "test.dsc");
            File.WriteAllText(filePath, "const x = 2;");

            ISourceFile sourceFile;
            var result = TestSuite.TryParseTestFile(Logger, filePath, out sourceFile);
            Assert.True(result);
        }

        [Fact]
        public void ParseFromDiskMissing()
        {
            var folder = GetAndCleanTestFolder(nameof(TestSuiteTests), nameof(ParseFromDiskMissing));
            var filePath = Path.Combine(folder, "missingFile.dsc");

            ISourceFile sourceFile;
            var result = TestSuite.TryParseTestFile(Logger, filePath, out sourceFile);
            Assert.False(result);
            Logger.ValidateErrors(1, "the file does not exist");
        }

        [Fact]
        public void ParseFromDiskLocked()
        {
            var folder = GetAndCleanTestFolder(nameof(TestSuiteTests), nameof(ParseFromDiskLocked));
            var filePath = Path.Combine(folder, "test.dsc");

            // this locks file and will disallow the logic reading it
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                ISourceFile sourceFile;
                var result = TestSuite.TryParseTestFile(Logger, filePath, out sourceFile);
                Assert.False(result);
                Logger.ValidateErrors(1, "Error reading file", "it is being used by another process");
            }
        }

        [Fact]
        public void ParseNormalFile()
        {
            ISourceFile sourceFile;

            var result = TestSuite.TryParseTestFile(Logger, "test.dsc", @"const x = 2;", out sourceFile);

            Assert.True(result);
            Assert.NotNull(sourceFile);
            Assert.Equal(7, sourceFile.NodeCount);
            Assert.Equal("const x = 2;", sourceFile.ToDisplayString());
        }

        [Fact]
        public void ParseSyntaxError()
        {
            ISourceFile sourceFile;

            var result = TestSuite.TryParseTestFile(Logger, "test.dsc", @"const x = 2<", out sourceFile);

            Assert.False(result);
            Logger.ValidateErrors(1, "Expression expected");
        }

        [Fact]
        public void BindError()
        {
            ISourceFile sourceFile;

            var result = TestSuite.TryParseTestFile(Logger, "test.dsc", @"function x() { return 1; return 2; }", out sourceFile);

            Assert.False(result);
            Logger.ValidateErrors(1, "Unreachable code detected");
        }

        [Fact]
        public void MultipleTestFiles()
        {
            var folder = GetAndCleanTestFolder(nameof(TestSuiteTests), nameof(MultipleTestFiles));
            var file1Path = Path.Combine(folder, "test1.dsc");
            var file2Path = Path.Combine(folder, "Folder", "test2.dsc");
            Directory.CreateDirectory(Path.Combine(folder, "Folder"));
            File.WriteAllText(file1Path, "@@unitTest export function testFun1() {}");
            File.WriteAllText(file2Path, "@@unitTest export function testFun2() {}");

            TestSuite testSuite;
            var result = TestSuite.TryCreateTestSuite(
                Logger,
                new[] { file1Path, file2Path },
                new Dictionary<string, string>(0),
                out testSuite);

            Assert.True(result);
            Assert.Equal(2, testSuite.Classes.Count);

            var testClass1 = testSuite.Classes[0];
            Assert.Equal("test1", testClass1.Name);
            Assert.Equal(1, testClass1.Functions.Count);
            Assert.Equal("testFun1", testClass1.Functions[0].FullIdentifier);

            var testClass2 = testSuite.Classes[1];
            Assert.Equal("test2", testClass2.Name);
            Assert.Equal(1, testClass2.Functions.Count);
            Assert.Equal("testFun2", testClass2.Functions[0].FullIdentifier);
        }

        [Fact]
        public void DuplicateTestNameNotAllowed()
        {
            var folder = GetAndCleanTestFolder(nameof(TestSuiteTests), nameof(DuplicateTestNameNotAllowed));
            var file1Path = Path.Combine(folder, "test.dsc");
            var file2Path = Path.Combine(folder, "Folder", "test.dsc");
            Directory.CreateDirectory(Path.Combine(folder, "Folder"));
            File.WriteAllText(file1Path, "@@unitTest export function testFun1() {}");
            File.WriteAllText(file2Path, "@@unitTest export function testFun2() {}");

            TestSuite testSuite;
            var result = TestSuite.TryCreateTestSuite(Logger, new[] { file1Path, file2Path }, new Dictionary<string, string>(0), out testSuite);

            Assert.False(result);
            Logger.ValidateErrors(1, "Duplicate test name: 'test'");
        }

        [Fact]
        public void NoTestNotAllowed()
        {
            TestSuite testSuite;
            var result = TestSuite.TryCreateTestSuite(Logger, new string[0], new Dictionary<string, string>(0), out testSuite);

            Assert.False(result);
            Logger.ValidateErrors(1, "No test classes added to suite.");
        }
    }
}
