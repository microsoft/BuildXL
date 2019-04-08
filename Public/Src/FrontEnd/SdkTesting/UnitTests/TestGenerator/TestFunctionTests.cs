// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Testing.TestGenerator;
using TypeScript.Net.Binding;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    public sealed class TestFunctionTests : BaseTest
    {
        public TestFunctionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TopLevelFunction()
        {
            TestSuccess(
                @"
                export function short() { }
                ",
                "short",
                "short");
        }

        [Fact]
        public void OneNestedFunction()
        {
            TestSuccess(
                @"
                namespace Ns1 {
                    export function short() { }
                }",
                "short",
                "Ns1.short");
        }

        [Fact]
        public void MultiNestedFunction()
        {
            TestSuccess(
                @"
                namespace Ns1 {
                    export function short() { }
                }",
                "short",
                "Ns1.short");
        }

        [Fact]
        public void LkgFile()
        {
            var lkgFile = A("x", "folder", "testFile", "short.lkg");
            var fun = TestSuccess(
                @"
                namespace Ns1 {
                    export function short() { }
                }",
                "short",
                "Ns1.short",
                lkgFile);
            Assert.Equal(lkgFile, fun.LkgFilePath);
        }

        [Fact]
        public void MustExport()
        {
            TestFailure(
                @"
                function short() {}
                ",
                1,
                "function 'short' must be exported");
        }

        [Fact]
        public void MustBeVoid()
        {
            TestFailure(
                @"
                export function short() : string { }
                ",
                1,
                "function 'short' cannot return a value");
        }

        [Fact]
        public void MustDeclareBody()
        {
            TestFailure(
                @"
                export declare function short()
                ",
                1,
                "function 'short' must declare a function body");
        }

        [Fact]
        public void MustNotHaveParameters()
        {
            TestFailure(
                @"
                export function short(x:string) {}
                ",
                1,
                "'short' cannot have any parameters");
        }

        [Fact]
        public void MustNotHaveTypeParameters()
        {
            TestFailure(
                @"
                export function short<T>() {}
                ",
                1,
                "function 'short' cannot be generic");
        }

        private TestFunction TestSuccess(string code, string expectedShortName, string expectedFullIdentifier, params string[] lkgFiles)
        {
            var lkgFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lkgFile in lkgFiles)
            {
                lkgFileMap.Add(Args.ComputeLkgKey(lkgFile), lkgFile);
            }

            return TestHelper(code, expectedShortName, expectedFullIdentifier, 0, lkgFileMap);
        }

        private void TestFailure(string code, int expectedErrorCount, params string[] expectedMessages)
        {
            TestHelper(code, null, null, expectedErrorCount, new Dictionary<string, string>(0), expectedMessages);
        }

        private TestFunction TestHelper(string code, string expectedShortName, string expectedFullIdentifier, int expectedErrorCount, Dictionary<string, string> lkgFiles, params string[] expectedMessages)
        {
            TestFunction testFunction = null;
            var parser = new Parser();
            ISourceFile sourceFile = parser.ParseSourceFileContent("testFile.dsc", code, ParsingOptions.DefaultParsingOptions);
            Assert.Equal(0, sourceFile.ParseDiagnostics.Count);

            var binder = new Binder();
            binder.BindSourceFile(sourceFile, CompilerOptions.Empty);
            Assert.Equal(0, sourceFile.BindDiagnostics.Count);

            foreach (var node in NodeWalker.TraverseBreadthFirstAndSelf(sourceFile))
            {
                if (node.Kind == SyntaxKind.FunctionDeclaration)
                {
                    var functionDecl = (IFunctionDeclaration)node;
                    var success = TestFunction.TryExtractFunction(Logger, sourceFile, functionDecl, lkgFiles, out testFunction);
                    Assert.Equal(expectedErrorCount == 0, success);
                    if (success)
                    {
                        Assert.NotNull(testFunction);
                        Assert.Equal(expectedShortName, testFunction.ShortName);
                        Assert.Equal(expectedFullIdentifier, testFunction.FullIdentifier);
                    }
                }
            }

            Logger.ValidateErrors(expectedErrorCount, expectedMessages);

            return testFunction;
        }
    }
}
