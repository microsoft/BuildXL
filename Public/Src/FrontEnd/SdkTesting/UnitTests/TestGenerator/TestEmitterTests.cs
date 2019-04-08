// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.CodeGenerationHelper;
using BuildXL.FrontEnd.Script.Testing.TestGenerator;
using TypeScript.Net.Types;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    public class TestEmitterTests : BaseTest
    {
        public TestEmitterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestClassEmit()
        {
            var stringWriter = new StringWriter();
            var gen = new CodeGenerator(t => stringWriter.Write(t));

            var testClass = new TestClass("testName",  A("x", "test.dsc"),
                new TestFunction("test1", "test1", default(LineAndColumn), A("x", "test.test1.lkg")),
                new TestFunction("test2", "Ns2.test2", default(LineAndColumn), null));

            TestEmitter.WriteTestClass(gen, testClass, new[] {
                A("x", "Sdk1"),
                A("x", "Sdk2")
            });

            var resultLines = stringWriter
                .GetStringBuilder()
                .ToString()
                .Split('\r', '\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .ToArray();
            Assert.Contains("// Copyright (c) Microsoft Corporation. All rights reserved.", resultLines);
            Assert.Contains("using BuildXL.FrontEnd.Script.Testing.Helper;", resultLines);
            Assert.Contains("using Xunit;", resultLines);
            Assert.Contains("using Xunit.Abstractions;", resultLines);
            Assert.Contains("public sealed class @testName : UnitTestBase", resultLines);
            Assert.Contains("public @testName(ITestOutputHelper output)", resultLines);

            if (OperatingSystemHelper.IsUnixOS)
            {
                Assert.Contains(String.Format(@"protected override string FileUnderTest => ""{0}"";", A("x", "test.dsc")), resultLines);
                Assert.Contains("protected override string[] SdkFoldersUnderTest => new string[] {", resultLines);
                Assert.Contains(String.Format(@"""{0}"",", A("x", "Sdk1")), resultLines);
                Assert.Contains(String.Format(@"""{0}"",", A("x", "Sdk2")), resultLines);
                Assert.Contains(String.Format(@"public void test1() => RunSpecTest(""test1"", ""test1"", ""{0}"");", A("x", "test.test1.lkg")), resultLines);
            }
            else
            {
                Assert.Contains(String.Format(@"protected override string FileUnderTest => @""{0}"";", A("x", "test.dsc")), resultLines);
                Assert.Contains("protected override string[] SdkFoldersUnderTest => new string[] {", resultLines);
                Assert.Contains(String.Format(@"@""{0}"",", A("x", "Sdk1")), resultLines);
                Assert.Contains(String.Format(@"@""{0}"",", A("x", "Sdk2")), resultLines);
                Assert.Contains(String.Format(@"public void test1() => RunSpecTest(""test1"", ""test1"", @""{0}"");", A("x", "test.test1.lkg")), resultLines);
            }
            Assert.Contains(@"public void test2() => RunSpecTest(""Ns2.test2"", ""test2"");", resultLines);
        }

        [Fact]
        public void TestSuite()
        {
            var folder = GetAndCleanTestFolder(nameof(TestEmitterTests), nameof(TestSuite));
            var testSuite = new TestSuite(
                new TestClass("test1", A("x", "test1.dsc")),
                new TestClass("test2", A("x", "test2.dsc")));

            var success = TestEmitter.WriteTestSuite(Logger, testSuite, folder, new string[0]);
            Assert.True(success);

            var csFile1 = Path.Combine(folder, "test1.g.cs");
            var csFile2 = Path.Combine(folder, "test2.g.cs");
            Assert.True(File.Exists(csFile1));
            Assert.True(File.Exists(csFile2));

            Assert.Equal(0, Logger.ErrorCount);
        }

        [Fact]
        public void HandlesIoErrors()
        {
            var folder = GetAndCleanTestFolder(nameof(TestEmitterTests), nameof(HandlesIoErrors));
            var testSuite = new TestSuite(
                new TestClass("test1", A("x", "test1.dsc")));

            var csFile1 = Path.Combine(folder, "test1.g.cs");

            using (var fs = new FileStream(csFile1, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                var success = TestEmitter.WriteTestSuite(Logger, testSuite, folder, new string[0]);
                Assert.False(success);
                Logger.ValidateErrors(1, "Failed to write output file '", Path.DirectorySeparatorChar + "test1.g.cs' because it is being used by another process.");
            }
        }
    }
}
