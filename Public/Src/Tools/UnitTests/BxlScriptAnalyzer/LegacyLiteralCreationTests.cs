// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Analyzer.Analyzers;
using BuildXL.Utilities;
using Xunit;

namespace Test.Tool.DScript.Analyzer
{
    public class LegacyLiteralCreationTests
        : AnalyzerTest<LegacyLiteralCreation>
    {
        internal static readonly string PathPrefix = OperatingSystemHelper.IsUnixOS ? "/TestModule/" : "C:\\TestModule\\";

        [Fact]
        public void FixPathAtom()
        {
            TestSuccess(
                "const x = PathAtom.create('hello');",
                "const x = a`hello`;");
        }

        [Fact]
        public void FixPathAtomExpression()
        {
            TestSuccess(
                "const name = 'world';\r\nconst x = PathAtom.create('hello' + name);",
                "const name = 'world';\r\nconst x = a`hello${name}`;");
        }

        [Fact]
        public void FixPathAtomAddMultipleLiterals()
        {
            TestSuccess(
                "const x = PathAtom.create('a' + 'b' + 'c');",
                "const x = a`abc`;");
        }

        [Fact]
        public void FixPathAtomAddStringAndExpr()
        {
            TestSuccess(
                "const z = 'z';\r\nconst x = PathAtom.create('a' + z);",
                "const z = 'z';\r\nconst x = a`a${z}`;");
        }

        [Fact]
        public void FixPathAtomAddExprAndString()
        {
            TestSuccess(
                "const z = 'z';\r\nconst x = PathAtom.create(z + 'a');",
                "const z = 'z';\r\nconst x = a`${z}a`;");
        }

        [Fact]
        public void FixPathAtomComplexAdd()
        {
            TestSuccess(
                "const z = 'z';\r\nconst x = PathAtom.create('a' + 'b' + 'c' + z + z + 'e' + z + 'f');",
                "const z = 'z';\r\nconst x = a`abc${z}${z}e${z}f`;");
        }

        [Fact]
        public void DetectPathAtom()
        {
            TestErrorReport(
                "const x = PathAtom.create('hello');",
                $"{PathPrefix}0.dsc(1,11): Use a`hello` rather than PathAtom.create('hello')");
        }

        [Fact]
        public void FixRelativePath()
        {
            TestSuccess(
                "const x = RelativePath.create('a/b/c');",
                "const x = r`a/b/c`;");
        }

        [Fact]
        public void DetectRelativePath()
        {
            TestErrorReport(
                "const x = RelativePath.create('a/b/c');",
                $"{PathPrefix}0.dsc(1,11): Use r`a/b/c` rather than RelativePath.create('a/b/c')");
        }
    }
}
