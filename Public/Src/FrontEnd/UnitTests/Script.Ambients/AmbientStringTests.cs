// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientStringTests : DsTest
    {
        public AmbientStringTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("buildxl", 0, "b")]
        [InlineData("buildxl", -1, null)]
        [InlineData("buildxl", 5, "x")]
        [InlineData("buildxl", 10, null)]
        public void TestCharAt(string stringLiteral, int position, string expectedResult)
        {
            var result = EvaluateSpec(
                string.Format("export const ans = \"{0}\".charAt({1});", stringLiteral, position), 
                new[] { "ans" });
            if (expectedResult == null)
            {
                result.ExpectErrorCode((int)LogEventId.StringIndexOufOfRange, count: 1);
            }
            else
            {
                result.ExpectNoError();
                result.ExpectValues(count: 1);
                Assert.Equal(expectedResult, result.Values[0]);
            }
        }

        [Theory]
        [InlineData("buildxl", 0, 98)]
        [InlineData("buildxl", 5, 120)]
        [InlineData("buildxl", -1, -1)]
        [InlineData("buildxl", 10, -1)]
        public void TestCharCodeAt(string stringLiteral, int position, int expectedResult)
        {
            var result = EvaluateSpec(
                string.Format("export const ans = \"{0}\".charCodeAt({1});", stringLiteral, position), 
                new[] { "ans" });
            if (expectedResult == -1)
            {
                result.ExpectErrorCode((int)LogEventId.StringIndexOufOfRange, count: 1);
            }
            else
            {
                result.ExpectValues(count: 1);
                result.ExpectNoError();
                Assert.Equal(expectedResult, result.Values[0]);
            }
        }

        [Theory]
        [InlineData("  aa bb     ", "trim", null, "aa bb")]
        [InlineData("  aa bb     ", "trimStart", null, "aa bb     ")]
        [InlineData("  aa bb     ", "trimEnd", null, "  aa bb")]
        [InlineData("aa bb", "trim", "a", " bb")]
        [InlineData("aa bb", "trim", "b", "aa ")]
        [InlineData("aa bb", "trim", "ab", " ")]
        [InlineData("aa bb", "trimStart", "a", " bb")]
        [InlineData("aa bb", "trimStart", "b", "aa bb")]
        [InlineData("aa bb", "trimStart", "ab", " bb")]
        [InlineData("aa bb", "trimEnd", "a", "aa bb")]
        [InlineData("aa bb", "trimEnd", "b", "aa ")]
        [InlineData("aa bb", "trimEnd", "ab", "aa ")]

        // these have tabs
        [InlineData("	 aa	bb 	", "trim", null, "aa\tbb")]
        [InlineData("	 aa	bb 	", "trimStart", null, "aa\tbb \t")]
        [InlineData("	 aa	bb 	", "trimEnd", null, "\t aa\tbb")]
        public void TestTrims(string str, string op, string arg, string expectedResult)
        {
            var argStr = arg != null ? '"' + arg + '"' : string.Empty;
            var result = EvaluateSpec(
                string.Format("export const ans = \"{0}\".{1}({2});", str, op, argStr), 
                new[] { "ans" });
            result.ExpectValues(count: 1);
            result.ExpectNoError();
            Assert.Equal(expectedResult, result.Values[0]);
        }

        [Fact]
        public void TestStringSplit()
        {
            var result = EvaluateSpec(@"
namespace M {
    const str1 : String = "";aa;;bb;"";
    const str2 : String = ""aaSEPbbSEPcc"";
    
    export const r1 = str1.split("";"");
    export const r2 = str1.split("";"", 3);
    export const r3 = str2.split(""SEP"");
    
}", new[] {"M.r1", "M.r2", "M.r3"});

            result.ExpectNoError();
            result.ExpectValues(count: 3);

            AssertArraysEqual(new[] {string.Empty, "aa", string.Empty, "bb", string.Empty}, result.Values[0]);
            AssertArraysEqual(new[] {string.Empty, "aa", string.Empty}, result.Values[1]);
            AssertArraysEqual(new[] {"aa", "bb", "cc"}, result.Values[2]);
        }

        [Fact]
        public void TestStringLength()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const n: number = ""abcdefghij"".length;
}", new[] { "M.n" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(10, result.Values[0]);
        }

        [Theory]
        [InlineData("buildxl", "buildxl", 0)]
        [InlineData("buildxl", "BuildXL", -1)]
        [InlineData("BuildXL", "buildxl", 1)]
        [InlineData("BuildXL", "", 1)]
        [InlineData("BuildXL", null, -1)]
        [InlineData("", "", 0)]
        [InlineData("", "42", -1)]
        public void TestLocaleCompare(string lhs, string rhs, int expectedResult)
        {
            var rhsLiteral = rhs == null ? "undefined" : '"' + rhs + '"';
            var result = EvaluateSpec(
                string.Format("export const ans = \"{0}\".localeCompare({1});", lhs, rhsLiteral), 
                new[] { "ans" });
            result.ExpectValues(count: 1);
            result.ExpectNoError();
            Assert.Equal(expectedResult, result.Values[0]);
        }

        [Theory]
        [InlineData("buildxl", "x", null, 5)]
        [InlineData("buildxl", "x", 10, 5)]
        [InlineData("buildxl", "x", 5, 5)]
        [InlineData("buildxl", "l", 4, 3)]
        [InlineData("buildxl", "u", 1, 1)]
        [InlineData("buildxl", "l", 0, -1)]
        [InlineData("buildxl", "l", -10, -1)]
        [InlineData("buildxl", "z", null, -1)]
        [InlineData("buildxl", "b", null, 0)]
        public void TestLastIndexOf(string lhs, string rhs, int? position, int expectedResult)
        {
            var pos = position != null ? $", {position.Value}" : string.Empty;
            var result = EvaluateSpec(string.Format("export const ans = \"{0}\".lastIndexOf(\"{1}\"{2});", lhs, rhs, pos), new[] { "ans" });
            result.ExpectValues(count: 1);
            result.ExpectNoError();
            Assert.Equal(expectedResult, result.Values[0]);
        }

        [Theory]
        [InlineData("buildxl", null, null, "buildxl")]
        [InlineData("buildxl", 0, null, "buildxl")]
        [InlineData("buildxl", 0, 6, "buildx")]
        [InlineData("buildxl", 0, 5, "build")]
        [InlineData("buildxl", 1, 5, "uild")]
        [InlineData("buildxl", 2, 5, "ild")]
        [InlineData("buildxl", 3, 4, "l")]
        [InlineData("buildxl", 3, 3, "")]
        [InlineData("buildxl", 4, 3, "")]
        [InlineData("buildxl", -2, 3, "")]
        [InlineData("buildxl", 2, 23, "ildxl")]
        public void TestSlice(string lhs, int? start, int? end, string expectedResult)
        {
            var startStr = start != null ? $"{start.Value}" : string.Empty;
            var endStr = end != null ? $", {end.Value}" : string.Empty;
            var result = EvaluateSpec(
                string.Format("export const ans = \"{0}\".slice({1}{2});", lhs, startStr, endStr), 
                new[] { "ans" });
            result.ExpectValues(count: 1);
            result.ExpectNoError();
            Assert.Equal(expectedResult, result.Values[0]);
        }

        [Fact]
        public void TestStringToArray()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const r1 = ""buildxl"".toArray();
    export const r2 = """".toArray();
    
}", new[] { "M.r1", "M.r2" });

            result.ExpectNoError();
            result.ExpectValues(count: 2);

            AssertArraysEqual(new[] { "b", "u", "i", "l", "d", "x", "l" }, result.Values[0]);
            AssertArraysEqual(new string[0], result.Values[1]);
        }

        [Fact]
        public void TestFromCharCode()
        {
            var result = EvaluateSpec("export const ans = String.fromCharCode([97, 98, 99]);", new[] { "ans" });
            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal("abc", result.Values[0]);
        }

#if DEBUG
        // There are few tests that relies on non-production API to check some corner cases.
        // For instance, there was a Bug #801807 when interpreter didn't deal correctly with names like '__foo'.
        // So we have some special api that is visible only in debug build to prevent pollution of the public surface.

        [Fact]
        public void NamespaceLevelFunctionThatStartsWithUnderscoreShouldBeResolvedCorrectly()
        {
            string code =
@"
const x = '';
const y = 'z';
export const r1 = String.__isUndefinedOrEmpty(x); // true
export const r2 = String.__isUndefinedOrEmpty(y); // false
";
            var result = Build()
                .AddExtraPreludeSpec(@"
/// <reference path=""Prelude.Core.dsc""/>
namespace String {
    export declare function __isUndefinedOrEmpty(x: string): boolean;
}
")
                .AddSpec(MainSpecRelativePath, code)
                .Evaluate("r1", "r2");

            Assert.True((bool)result.Values[0]);
            Assert.False((bool)result.Values[1]);
        }

        [Fact]
        public void InstanceLevelFunctionThatStartsWithUnderscoreShouldBeResolvedCorrectly()
        {

            string code =
@"const y = 'z';
export const r1 = y.__charAt(0); // 'z'
export const r2 = y.__length; // 1
";

    var result = Build()
        .AddExtraPreludeSpec(@"
/// <reference path=""Prelude.Core.dsc""/>
interface String
{
    __charAt(i: number): string;
    __length(): number;
}")
        .AddSpec(MainSpecRelativePath, code)
        .Evaluate("r1", "r2");

    Assert.Equal("z", result.Values[0]);
    Assert.Equal(1, result.Values[1]);
}
#endif

        [Fact]
        public void TestStringInterpolation()
        {
            var result = EvaluateSpec(@"
const x = a`atom`;
const s = ""hello"";
const n = 7;
const o = { f: n };
export const ans = `prefix;${x};${s};${n};${o};suffix`;", new[] { "ans" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal("prefix;atom;hello;7;{f: 7};suffix", result.Values[0]);
        }

        private static void AssertArraysEqual(string[] expected, object actual)
        {
            var actualArray = (actual as ArrayLiteral).Values.Select(v => v.Value).ToArray();
            XAssert.ArrayEqual(expected, actualArray);
        }
    }
}
