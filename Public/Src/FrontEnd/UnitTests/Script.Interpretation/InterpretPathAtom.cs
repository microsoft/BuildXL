// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class InterpretPathAtom : DsTest
    {
        public InterpretPathAtom(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestPathAtomCreate()
        {
            string code = @"export const r = PathAtom.create(""test.exe"");";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(PathAtom.Create(StringTable, "test.exe"), result);
        }

        [Theory]
        [InlineData("test.exe", "\".dll\"", "test.dll")]
        [InlineData("test", "\".dll\"", "test.dll")]
        [InlineData("test.dll", "\"\"", "test")]
        [InlineData("test.exe", "PathAtom.create(\".dll\")", "test.dll")]
        public void TestPathAtomChangeExtension(string originalAtom, string newExtension, string expectedAtom)
        {
            const string CodeTemplate = @"
const p1 = PathAtom.create(""{0}"");
export const r = p1.changeExtension({1}).toString();";

            string code = string.Format(CodeTemplate, originalAtom, newExtension);
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(expectedAtom, result);
        }

        [Fact]
        public void TestPathAtomGetExtension()
        {
            string spec = @"export const r = PathAtom.create(""test.exe"").extension.toString();";

            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(".exe", result);
        }

        [Fact]
        public void TestPathAtomHasExtension()
        {
            string spec = @"
export const hasExtension = PathAtom.create(""test.exe"").hasExtension;
export const hasNoExtension = PathAtom.create(""test"").hasExtension;";

            var result = EvaluateExpressionsWithNoErrors(spec, "hasExtension", "hasNoExtension");
            Assert.Equal(true, result["hasExtension"]);
            Assert.Equal(false, result["hasNoExtension"]);
        }

        [Fact]
        public void PathAtomHasExtensionProperty()
        {
            string spec =
@"export const r = PathAtom.create(""file.txt"").extension.toString();";
            var result = EvaluateExpressionWithNoErrors(spec, "r");
            Assert.Equal(".txt", result);
        }

        [Fact]
        public void TestPathAtomConcat()
        {
            string code = @"export const r = a`test`.concat(a`Mydll.dll`).toString();";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal("testMydll.dll", result);
        }

        [Theory]
        [InlineData("Test.exe", "Test.exe", true, true)]
        [InlineData("Test.exe", "test.exe", false, true)]
        public void TestEquality(string left, string right, bool caseSensitiveEquals, bool caseInsensitiveEquals)
        {
            const string CodeTemplate = @"
const p1 = PathAtom.create(""{0}"");
const p2 = PathAtom.create(""{1}"");

export const caseSensitive = p1.equals(p2, false);
export const caseSensitiveWithString = p1.equals(""{1}"", false);
export const caseInsensitive = p1.equals(p2, true);
export const caseInsensitiveWithString = p1.equals(""{1}"", true);";

            string spec = string.Format(CodeTemplate, left, right);
            var result = EvaluateExpressionsWithNoErrors(
                spec,
                "caseSensitive",
                "caseSensitiveWithString",
                "caseInsensitive",
                "caseInsensitiveWithString");

            Assert.Equal(caseSensitiveEquals, result["caseSensitive"]);
            Assert.Equal(caseSensitiveEquals, result["caseSensitiveWithString"]);
            Assert.Equal(caseInsensitiveEquals, result["caseInsensitive"]);
            Assert.Equal(caseInsensitiveEquals, result["caseInsensitiveWithString"]);
        }

        [Fact]
        public void EqualityShouldBeCaseSensitiveByDefault()
        {
            string code = @"
const p1 = PathAtom.create(""Test.cs"");
const p2 = PathAtom.create(""test.cs"");

export const caseInsensitive = p1.equals(p2, /*ignoreCase*/true);
export const caseSensitive = p1.equals(p2);";

            var result = EvaluateExpressionsWithNoErrors(code, "caseSensitive", "caseInsensitive");

            Assert.Equal(false, result["caseSensitive"]);
            Assert.Equal(true, result["caseInsensitive"]);
        }

        [Fact]
        public void TestPathEquals()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const testExe1 = PathAtom.create(""test.exe"");
    export const testExe2 = PathAtom.create(""test.exe"");
    export const testEXE = PathAtom.create(""test.EXE"");
    export const testExeString = ""test.exe"";
    export const testEXEString = ""test.EXE"";

    export const e1 = testExe1.equals(testExe2);
    export const e2 = testExe1.equals(testEXE);
    export const e3 = testExe1.equals(testEXE, true);
    export const e4 = testExe1.equals(testEXE, false);
    export const e5 = testExe1.equals(testExeString);
    export const e6 = testExe1.equals(testEXEString);
    export const e7 = testExe1.equals(testEXEString, true);
    
}", new[] { "M.e1", "M.e2", "M.e3", "M.e4", "M.e5", "M.e6", "M.e7" });

            result.ExpectErrors(0);
            result.ExpectValues(7);

            Assert.Equal(result.Values[0], true);
            Assert.Equal(result.Values[1], false);
            Assert.Equal(result.Values[2], true);
            Assert.Equal(result.Values[3], false);
            Assert.Equal(result.Values[4], true);
            Assert.Equal(result.Values[5], false);
            Assert.Equal(result.Values[6], true);
        }

        [Fact]
        public void TestSimplePathAtomInterpolation()
        {
            string spec =
                @"
const x = PathAtom.create(""file.txt"");
const y = a`file.txt`;
const z = (x === y);
";
            var result = EvaluateExpressionWithNoErrors(spec, "z");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathAtomInterpolationFromString()
        {
            string spec =
                @"
const w: string = ""file.txt"";
const x = PathAtom.create(w);
const y = a`${w}`;
const z = (x === y);
";
            var result = EvaluateExpressionWithNoErrors(spec, "z");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathAtomConcatenation()
        {
            string spec =
                @"
const w = a`file`;
const x = a`.txt`;
const y = a`${w}${x}.bak`;
const z = (a`file.txt.bak` === y);
";
            var result = EvaluateExpressionWithNoErrors(spec, "z");
            Assert.Equal(true, result);
        }
    }
}
