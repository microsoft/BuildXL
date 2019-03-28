// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Test.DScript.Ast.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientObjectTests : DsTest
    {
        public AmbientObjectTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestKeysMember()
        {
            string code = @"
const x = {x: 42, y: 32};
export const r = x.keys();";

            var result = (ArrayLiteral) EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(new[] {"x", "y"}, result.ValuesAsObjects().OfType<string>().ToArray());
        }

        /// <summary>
        /// Expected result compared against doing a toString() on the object and discarding trivia
        /// </summary>
        [Theory]
        [InlineData("{a: 1, b: 2}", "{c: 3}", "{a:1,b:2,c:3}")]
        [InlineData("{a: 1, b: 2}", "{b: 3}", "{a:1,b:3}")]
        [InlineData("{a: 1, b: 2}", "3", "3")]
        [InlineData("{a: 1, b: 2}", "[6, 7]", "[6,7]")]
        [InlineData("{a: 1, b: [0, 1], c: 3}", "{a: 5, b: [2, 3], d: 6}", "{a:5,b:[0,1,2,3],c:3,d:6}")]
        [InlineData("{a: 1, b: {d: 4}, c: 3}", "{b: {d: 5}}", "{a:1,b:{d:5},c:3}")]
        [InlineData("{a: 1, b: {d: [4]}, c: 3}", "{b: {d: [5]}}", "{a:1,b:{d:[4,5]},c:3}")]
        [InlineData("{a: 1, b: {d: 1}, c: 3}", "{b: {e: 2}}", "{a:1,b:{d:1,e:2},c:3}")]
        [InlineData("{a: 1}", "undefined", "{a:1}")]
        public void TestMerge(string leftObject, string rightObject, string expectedResult)
        {
            var result = EvaluateSpec(
                $@"
const o1 = {leftObject};
const o2 = {rightObject};
export const result = o1.merge(o2).toString();
",
                new[] {"result"});

            result.ExpectNoError();

            var actualResult = (string) result.Values[0];
            AssertEqualDiscardingTrivia(expectedResult, actualResult);
        }

        /// <summary>
        /// Expected result compared against doing a toString() on the object and discarding trivia
        /// </summary>
        [Theory]
        [InlineData("{a: 1, b: 2}", "{c: 3}", "{a:1,b:2,c:3}")]
        [InlineData("{a: 1, b: 2}", "{b: 3}", "{a:1,b:3}")]
        [InlineData("{a: 1, b: 2}", "3", "3")]
        [InlineData("{a: 1, b: 2}", "[6, 7]", "[6,7]")]
        [InlineData("{a: 1, b: [0, 1], c: 3}", "{a: 5, b: [2, 3], d: 6}", "{a:5,b:[2,3],c:3,d:6}")]
        [InlineData("{a: 1, b: {d: 4}, c: 3}", "{b: {d: 5}}", "{a:1,b:{d:5},c:3}")]
        [InlineData("{a: 1, b: {d: [4]}, c: 3}", "{b: {d: [5]}}", "{a:1,b:{d:[5]},c:3}")]
        [InlineData("{a: 1, b: {d: 1}, c: 3}", "{b: {e: 2}}", "{a:1,b:{e:2},c:3}")]
        [InlineData("{a: 1}", "undefined", "{a:1}")]
        public void TestOverrideWithLeftAndRight(string leftObject, string rightObject, string expectedResult)
        {
            var result = EvaluateSpec(
                $@"
const o1 = {leftObject};
const o2 = {rightObject};
export const result = o1.override(o2).toString();
",
                new[] {"result"});

            result.ExpectNoError();

            var actualResult = (string) result.Values[0];
            AssertEqualDiscardingTrivia(expectedResult, actualResult);
        }

        [Theory]
        [InlineData("-1", "-1")]
        [InlineData("0", "0")]
        [InlineData("1", "1")]

        [InlineData("true", "true")]
        [InlineData("false", "false")]

        [InlineData("\"a\"", "a")]
        [InlineData("'a'", "a")]
        [InlineData("`a`", "a")]

        [InlineData("f`a`", "f`b:/a`")]
        [InlineData("p`a`", "p`b:/a`")]
        [InlineData("d`f/a`", "d`b:/f/a`")]
        [InlineData("a`a`", "a")]
        [InlineData("r`f/a`", "r`f/a`")]

        [InlineData("[1,2]", "[1,2]")]
        [InlineData("Set.empty<number>().add(1).add(2)", "<Set>[1,2]")]
        [InlineData("Map.empty<string, number>().add('a', 1).add('b', 2)", "<Map>[{key:\"a\",value:1},{key:\"b\",value:2}]")]
        public void TestOverrideWithLeft(string leftObject, string expectedResult)
        {
            var result = EvaluateSpec(
                $@"
const o1 = {leftObject};
export const result = o1.toString();
",
                new[] {"result"});

            result.ExpectNoError();

            var actualResult = (string) result.Values[0];
            if (expectedResult.Contains("b:"))
            {
                var testFolder = (TestRoot.Replace("\\", "/")).ToLowerInvariant();
                actualResult = actualResult.ToLowerInvariant().Replace(testFolder, "b:");
            }

            AssertEqualDiscardingTrivia(expectedResult, actualResult);
        }

        [InlineData("{a: 1}.withCustomMerge((a,b)=>b)", "{a: 2}", "{a:2}")]
        [InlineData("{a: 1}.withCustomMerge((a,b)=>b)", "42", "42")]
        [InlineData("{a: {b: 2}.withCustomMerge((a,b)=>42)}", "{a: 3, b: 4}", "{a:42,b:4}")]
        [InlineData("{a: 1}.withCustomMerge((a,b)=>0)", "{b: 1}.withCustomMerge((a,b)=>1)", "0")]
        [InlineData("{a: 1}", "{b: 1}.withCustomMerge((a,b)=>1)", "1")]
        [InlineData("{a: [1,2,3].withCustomMerge<number[]>((a, b) => b.concat(a))}", "{a: [4,5]}", "{a:[4,5,1,2,3]}")]
        [Theory]
        public void TestCustomMerge(string leftObject, string rightObject, string expectedResult)
        {
            var result = EvaluateSpec(
$@"
const o1 = {leftObject};
const o2 = {rightObject};
export const result = o1.merge(o2).toString();
",
                new[] { "result" });

            result.ExpectNoError();

            var actualResult = (string)result.Values[0];
            AssertEqualDiscardingTrivia(expectedResult, actualResult);
        }

        [Theory]
        [InlineData("{a: 1}.withCustomMerge(<any>42)", "{a: 2}", LogEventId.UnexpectedValueTypeOnConversion)]
        [InlineData("{a: 1}.withCustomMerge((a, b) => (<number> a) + (<number> b))", "{a: 2}", LogEventId.UnexpectedValueType)]
        [InlineData("{a: [1].withCustomMerge<number[]>((a, b) => b.concat(a))}", "{a: 2}", LogEventId.MissingInstanceMember)]
        [InlineData("{a: [1].withCustomMerge(undefined)}", "{a: 2}", LogEventId.UnexpectedValueTypeOnConversion)]
        public void TestCustomMergeFailures(string leftObject, string rightObject, LogEventId expectedId)
        {
            var result = EvaluateSpec(
                $@"
const o1 = {leftObject};
const o2 = {rightObject};
export const result = o1.merge(o2);
",
                new[] { "result" });

            result.Errors.ExpectErrorCode(expectedId);
        }

        [Theory]
        [InlineData("[1,2,3].appendWhenMerged()", "[4,5]", "[1,2,3,4,5]")]
        [InlineData("[1,2,3].appendWhenMerged()", "undefined", "undefined")]
        [InlineData("[1,2,3].appendWhenMerged()", "42", "42")]
        [InlineData("[1,2,3].prependWhenMerged()", "[4,5]", "[4,5,1,2,3]")]
        [InlineData("[1,2,3].prependWhenMerged()", "undefined", "undefined")]
        [InlineData("[1,2,3].prependWhenMerged()", "42", "42")]
        [InlineData("[1,2,3].replaceWhenMerged()", "[4,5]", "[4,5]")]
        [InlineData("[1,2,3].replaceWhenMerged()", "undefined", "undefined")]
        [InlineData("[1,2,3].replaceWhenMerged()", "42", "42")]
        [InlineData("[1,2,3]", "[4,5].prependWhenMerged()", "[4,5,1,2,3]")]
        public void TestNativeCustomMerge(string leftObject, string rightObject, string expectedResult)
        {
            var result = EvaluateSpec(
                $@"
const o1 = {{a :{leftObject}}};
const o2 = {{a: {rightObject}}};
export const mergeResult = o1.merge<{{a: number[]}}>(o2).a;
export const result = mergeResult === undefined ? 'undefined' : mergeResult.toString();
",
                new[] { "result" });

            result.ExpectNoError();
            var actualResult = (string)result.Values[0];
            AssertEqualDiscardingTrivia(expectedResult, actualResult);
        }

        //[Theory] Unfortunately the hacked up prelude doesn't support a static Object.merge function declaration.
        [InlineData("undefined")]
        [InlineData("undefined", "undefined")]
        [InlineData("undefined", "undefined", "undefined")]
        [InlineData("{a:1}", "{a:1}")]
        [InlineData("{a:1}", "undefined", "{a:1}")]
        [InlineData("{a:1}", "undefined", "undefined", "{a:1}")]
        [InlineData("{a:1}", "{a:1}", "undefined")]
        [InlineData("{a:1}", "{a:1}", "undefined", "undefined")]
        [InlineData("{a:1}", "undefined", "undefined", "{a:1}", "undefined", "undefined")]
        [InlineData("{a:1}", "{a:0}", "{a:1}")]
        [InlineData("{a:1}", "undefined", "{a:0}", "undefined", "{a:1}", "undefined")]
        [InlineData("{a:1}", "undefined", "{a:0}", "{a:1}", "undefined")]
        [InlineData("{a:1}", "{a:99}", "{a:0}", "{a:1}")]
        public void TestObjectMerge(string expectedResult, params string[] objects)
        {
            var result = EvaluateSpec(
                $@"
export const mergeResult = Object.merge({string.Join(", ", objects)});
export const result = mergeResult === undefined ? 'undefined' : mergeResult.toString();
",
                new[] { "result" });

            result.ExpectNoError();
            var actualResult = (string)result.Values[0];
            AssertEqualDiscardingTrivia(expectedResult, actualResult);
        }
    }
}
