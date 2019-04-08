// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Testing.TestGenerator;
using TypeScript.Net.Binding;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    public class TestClassTests : BaseTest
    {
        public TestClassTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void SingleNonDecoratedFunction()
        {
            TestSuccess(
                @"
                export function fun() {};
                @@unitTest
                export function ut() {};
                ",
                "ut");
        }

        [Fact]
        public void IgnoreDecoratedNonUtFunction()
        {
            TestSuccess(
                @"
                @@other
                export function fun() {};

                @@unitTest
                export function ut() {};
                ",
                "ut");
        }

        [Fact]
        public void SingleDecoratedUtFunction()
        {
            TestSuccess(
                @"
                @@unitTest
                export function ut() {};
                ",
                "ut");
        }

        [Fact]
        public void SingleDecoratedUtFunctionWithParens()
        {
            TestSuccess(
                @"
                @@unitTest()
                export function fun() {};
                ",
                "fun");
        }

        [Fact]
        public void SingleDecoratedUtFunctionWithNs()
        {
            TestSuccess(
                @"
                @@SomeNamespace.unitTest
                export function fun() {};
                ",
                "fun");
        }

        [Fact]
        public void SingleDecoratedUtFunctionWithWithNsAndParens()
        {
            TestSuccess(
                @"
                @@SomeNamespace.unitTest()
                export function fun() {};
                ",
                "fun");
        }

        [Fact]
        public void UnitTestAnnotationDouble()
        {
            TestFailure(
                @"
                @@unitTest
                @@unitTest
                export function fun() {};
                ",
                1,
                "Duplicate unitTest decorator. Only one is allowed.");
        }

        [Fact]
        public void UnitTestAnnotationParameters()
        {
            TestFailure(
                @"
                @@unitTest(1)
                export function fun() {};
                ",
                1,
                "UnitTest decorators are not allowed to have arguments");
        }

        [Fact]
        public void UnitTestAnnotationTypeParameters()
        {
            TestFailure(
                @"
                @@unitTest<string>()
                export function fun() {};
                ",
                1,
                "UnitTest decorators are not allowed to be generic");
        }

        [Fact]
        public void UnitTestAnnotationOnConst()
        {
            TestFailure(
                @"
                @@unitTest
                const x = 10;
                ",
                1,
                "UnitTest attribute is only allowed on top-level functions");
        }

        [Fact]
        public void UnitTestAnnotationOnNamespace()
        {
            TestFailure(
                @"
                @@unitTest
                namespace X {
                    export function fun() {}
                }
                ",
                1,
                "UnitTest attribute is only allowed on top-level functions");
        }

        [Fact]
        public void UnitTestAnnotationOnNonToplevelFunction()
        {
            TestFailure(
                @"
                function outer() {
                    @@unitTest
                    function fun() {}
                }
                ",
                1,
                "Only top-level functions are allowed to be declared as UnitTests");
        }

        [Fact]
        public void UnitTestMustHaveAtLeastOne()
        {
            TestFailure(
                @"
                function fun() {}
                ",
                1,
                "No UnitTests found in file");
        }

        [Fact]
        public void UnitTestAnnotationDuplicateName()
        {
            TestFailure(
                @"
                @@unitTest
                export function fun() {}
                namespace Ns1 {
                    @@unitTest
                    export function Fun() {}
                }
                ",
                1,
                "Duplicate test-definition. There are multiple tests with name 'Fun': 'fun' and 'Ns1.Fun'");
        }

        [Fact]
        public void MustHaveAtLeastOneTest()
        {
            TestFailure(
                @"
                export function fun() {};
                ",
                1,
                "No UnitTests found in file.");
        }

        private void TestSuccess(string code, params string[] fullIdentifiers)
        {
            TestHelper(code, fullIdentifiers, 0);
        }

        private void TestFailure(string code, int expectedErrorCount, params string[] expectedMessages)
        {
            TestHelper(code, null, expectedErrorCount, expectedMessages);
        }

        private void TestHelper(string code, string[] fullIdentifiers, int expectedErrorCount, params string[] expectedMessages)
        {
            var parser = new Parser();
            ISourceFile sourceFile = parser.ParseSourceFileContent("testFile.dsc", code, ParsingOptions.DefaultParsingOptions);
            Assert.Equal(0, sourceFile.ParseDiagnostics.Count);

            var binder = new Binder();
            binder.BindSourceFile(sourceFile, CompilerOptions.Empty);
            Assert.Equal(0, sourceFile.BindDiagnostics.Count);

            TestClass testClass;
            var success = TestClass.TryExtractTestClass(Logger, sourceFile, new Dictionary<string, string>(0), out testClass);
            Assert.Equal(expectedErrorCount == 0, success);

            if (success)
            {
                var functions = testClass.Functions;
                Assert.Equal(fullIdentifiers.Length, functions.Count);
                for (int i = 0; i < fullIdentifiers.Length; i++)
                {
                    Assert.Equal(fullIdentifiers[i], functions[i].FullIdentifier);
                }
            }

            Logger.ValidateErrors(expectedErrorCount, expectedMessages);
        }
    }
}
