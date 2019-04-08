// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;
using static Test.DScript.Ast.Interpretation.ArrayLiteralEqualityComparer;
using System.Linq;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientArrayTests : DsTest
    {
        public AmbientArrayTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestIsEmptyFunction()
        {
            string code = @"
export const shouldBeEmpty = [].isEmpty();
export const shouldNotBeEmpty = [1].isEmpty();";

            var result = EvaluateExpressionsWithNoErrors(code, "shouldBeEmpty", "shouldNotBeEmpty");

            Assert.Equal(true, result["shouldBeEmpty"]);
            Assert.Equal(false, result["shouldNotBeEmpty"]);
        }

        [Fact]
        public void TestToFunction()
        {
            string code = @"export const r = [1, 2].toString();";

            var result = EvaluateExpressionWithNoErrors(code, "r");

            XAssert.EqualIgnoreWhiteSpace("[1, 2]", result.ToString());
        }

        [Fact]
        public void TestMapSimpleInc()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, 2, 3].map(e => e + 1);
}
", new[] { "M.x" }), new object[] { 2, 3, 4 });
        }

        [Fact]
        public void TestMapIncClosureWithLocalVar()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, 2, 3].map((e) => {
        let inc = 1;
        return e + inc;
    });
}
", new[] { "M.x" }), new object[] { 2, 3, 4 });
        }

        [Fact]
        public void TestMapIncWithFalsies()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, false, 2, undefined, 3].map((e: any) => {
        let inc = 1;
        return e ? e + inc : -1;
    });
}
", new[] { "M.x" }), new object[] { 2, -1, 3, -1, 4 });
        }

        [Fact]
        public void TestMapClosureWithNoArgs()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, 2, 3].map(() => {
        let inc = 1;
        return inc;
    });
}
", new[] { "M.x" }), new object[] { 1, 1, 1 });
        }

        [Fact]
        public void TestMapClosureWithNoArgsInline()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, 2, 3].map(() => 1);
}
", new[] { "M.x" }), new object[] { 1, 1, 1 });
        }

        [Fact]
        public void TestMapOverEmpty()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [].map(e => 1);
}
", new[] { "M.x" }), new object[] {});
        }

        [Fact]
        public void TestMapOverEmptyWithBogusClosure()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [].map(e => e % 0);
}
", new[] { "M.x" }), new object[] { });
        }

        [Fact]
        public void TestMapNested()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, 2, 3].map((e) => {
        let tripled = [e, e, e];
        return tripled.map((q) => {
            let x = 1;
            return q + 1;
        });
    });
}
", new[] { "M.x" }), new object[]
{
    new object[] { 2, 2, 2 }, 
                                    new object[] { 3, 3, 3 }, 
                                    new object[] { 4, 4, 4 }
});
        }

        [Fact]
        public void TestMapMany1()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, 2, 3].mapMany(e => [e, e]);
}
", new[] { "M.x" }), new object[] { 1, 1, 2, 2, 3, 3 });
        }

        [Fact]
        public void TestMapManyToNothing()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, 2, 3].mapMany(e => []);
}
", new[] { "M.x" }), new object[] {});
        }

        [Fact]
        public void TestMapManyOverEmpty()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [].mapMany(e => undefined);
}
", new[] { "M.x" }), new object[] { });
        }

        [Fact]
        public void TestMapManyWrongMapFunctionType()
        {
            EvaluateSpec(@"
namespace M {
    export const x = [1].mapMany(e => <any>12);
}
", new[] { "M.x" }).ExpectErrors(1);
        }

        [Theory]
        [InlineData("[1, 2, 3]", "(e, i) => e + i", new[] {1, 3, 5})]
        [InlineData("[]", "(e, i) => e + i", new int[0])]
        [InlineData("[1, 2, 3]", "(e, i, arr) => e + i + arr.length", new[] { 4, 6, 8 })]
        [InlineData("[0, 0, 0]", "(e, i, arr) => i", new[] { 0, 1, 2 })]
        public void TestMapWithIndex(string array, string lambda, object expectedResult)
        {
            string spec = I($"namespace M {{ export const x = {array}.map({lambda}); }}");
            TestResult result = EvaluateSpec(spec, new[] { "M.x" });
            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.True(AreEqual(expectedResult, result.Values[0]));
        }
        
        [Theory]
        [InlineData("[1, 2, 3]", "(e, i) => e === i", new int[0])]
        [InlineData("[0, 1, 2]", "(e, i) => e === i", new[] { 0, 1, 2 })]
        [InlineData("[0, 2, 2]", "(e, i) => e === i", new[] { 0, 2 })]
        public void TestFilterWithIndex(string array, string lambda, object expectedResult)
        {
            string spec = I($"namespace M {{ export const x = {array}.filter({lambda}); }}");
            TestResult result = EvaluateSpec(spec, new[] { "M.x" });
            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.True(AreEqual(expectedResult, result.Values[0]));
        }

        [Fact]
        public void TestReduce()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, 2, 3].reduce((a, e) => a + e, 0);
}
", new[] { "M.x" }), 6);
        }

        [Fact]
        public void TestReduceFlatten()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [[1], [2, 2], [3, 3, 3]].reduce((acc, arr) => acc.concat(arr), []);
}
", new[] { "M.x" }), new object[] {1, 2, 2, 3, 3, 3});
        }

        [Theory]
        [InlineData("[1, 2, 3]", "(a, e, i) => a + e + i", 0, 9)]
        [InlineData("[]", "(a, e, i) => a + e + i", 0, 0)]
        [InlineData("[1, 2, 3]", "(acc, e, i, arr) => acc + e + i + arr.length", 0, 18)]
        public void TestReduceWithIndex(string array, string lambda, int init, int expectedResult)
        {
            string spec = I($"namespace M {{ export const x = {array}.reduce({lambda}, {init}); }}");
            TestResult result = EvaluateSpec(spec, new[] { "M.x" });
            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(expectedResult, result.Values[0]);
        }

        [Fact]
        public void TestMapWithError()
        {
            CheckResult(EvaluateSpec(@"
namespace M {
    export const x = [1, 0, 2].map(y => {
        return 2 % y;
    });
}
", new[] { "M.x" }), ErrorValue.Instance, errorCount: 1);
        }

        [Fact]
        public void TestGroupBy()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const x = [1, 2, 3, 4, 5, 6].groupBy(e => e > 3);
    export const xLen = x.length;
}", new[] { "M.x", "M.xLen" });
            result.ExpectNoError();
            Assert.Equal(2, result.Values[1]);
            var groups = result.Values[0] as ArrayLiteral;
            Assert.NotNull(groups);
            Assert.Equal(2, groups.Values.Count);
            var grp0 = groups.Values[0].Value as ObjectLiteral;
            var grp1 = groups.Values[1].Value as ObjectLiteral;
            Assert.NotNull(grp0);
            Assert.NotNull(grp1);
            Assert.Equal(false, grp0[CreateString("key")].Value);
            Assert.Equal(true, grp1[CreateString("key")].Value);
            Assert.True(AreEqual(new object[] { 1, 2, 3 }, grp0[CreateString("values")]));
            Assert.True(AreEqual(new object[] { 4, 5, 6 }, grp1[CreateString("values")]));
        }
           
        [Theory]
        [InlineData("[1, 2, 3, 4, 5, 0]", "(e) => e === 5", true, false)] // single match in a middle
        [InlineData("[1, 2, 3, 4, 5, 0]", "(e) => e === 0", true, false)] // single match at the end
        [InlineData("[1, 2, 3, 4, 5, 0]", "(e) => e === 1", true, false)] // single match at the start    
        [InlineData("[1, 2, 3, 4, 5, 0]", "(e) => e > 3",  true, false)] // multiple but not all matches
        [InlineData("[1, 2, 3, 4, 5, 0]", "(e) => e > 9",  false, false)] // no matches
        [InlineData("[1, 2, 3, 4, 5, 0]", "(e) => e >= 0", true, true)]  // all matches
        public void TestSomeAllEvery(string array, string lambda, bool someResult, bool allResult)
        {
            var result = EvaluateSpec(
                $@"
namespace M {{
    const arr = {array};
    const pred = {lambda};
    export const someResult = arr.some(pred);
    export const allResult = arr.all(pred);
    export const everyResult = arr.every(pred);
}}", new[] { "M.someResult", "M.allResult", "M.everyResult" });

            result.ExpectNoError();
            Assert.Equal(someResult, (bool)result.Values[0]);
            Assert.Equal(allResult,  (bool)result.Values[1]);
            Assert.Equal(allResult, (bool)result.Values[2]); // 'every' should behave exactly like 'all'
        }

        [Theory]
        [InlineData("[1, 2, 3]", 1, 0)]    // index of first
        [InlineData("[1, 2, 3]", 2, 1)]    // index of middle
        [InlineData("[1, 2, 3]", 3, 2)]    // index of last
        [InlineData("[1, 2, 3, 2]", 2, 1)] // multiple matches
        [InlineData("[1, 2, 3]", 11, -1)]  // no matches
        [InlineData("[1, 2, 3]", "undefined", -1)]        // index of undefined not found
        [InlineData("[undefined, 2, 3]", "undefined", 0)] // index of undefined first
        [InlineData("[1, 2, undefined]", "undefined", 2)] // index of undefined last
        [InlineData("[undefined, 2, 3]", 3, 2)]           // index of last with undefined elem
        [InlineData("[]", "undefined", -1)]               // index of undefined in empty array
        public void CheckIndexOfResult(string array, object objectToFind, int expectedResult)
        {
            string spec = I($"namespace M {{ export const x = {array}.indexOf({objectToFind}); }}");
            TestResult result = EvaluateSpec(spec, new[] { "M.x" });
            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.True(AreEqual(expectedResult, result.Values[0]));
        }

        [Theory]
        [InlineData("[]", new int[0])]
        [InlineData("[0]", new[] { 0 })]
        [InlineData("[0, 1, 2]", new[] { 0, 1, 2 })]
        [InlineData("[0, 1, 1, 2, 3, 3, 4]", new[] { 0, 1, 2, 3, 4 })]
        public void TestUnique(string array, object expectedResult)
        {
            string spec = I($"namespace M {{ export const x = {array}.unique(); }}");
            TestResult result = EvaluateSpec(spec, new[] { "M.x" });
            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.True(AreEqual(expectedResult, result.Values[0]));
        }

        [Theory]
        [InlineData("[1, 2, 3, 4, 5]", 0, 10, new[] { 1, 2, 3, 4, 5 })]
        [InlineData("[1, 2, 3, 4, 5]", -10, 5, new[] {1, 2, 3, 4, 5 })]
        [InlineData("[1, 2, 3, 4, 5]", 0, 0, new int[0])]
        [InlineData("[1, 2, 3, 4, 5]", 0, 2, new[] { 1, 2 })]
        [InlineData("[1, 2, 3, 4, 5]", 2, 4, new[] { 3, 4 })]
        [InlineData("[1, 2, 3, 4, 5]", -2, 5, new[] { 4, 5 })]
        [InlineData("[1, 2, 3, 4, 5]", -2, -1, new[] { 4 })]
        [InlineData("[1, 2, 3, 4, 5]", -2, -4, new int[0])]
        public void CheckSlice(string array, int start, int end, int[] expectedResult)
        {
            string spec = I($"namespace M {{ export const x = {array}.slice({start}, {end}); }}");
            TestResult result = EvaluateSpec(spec, new[] { "M.x" });
            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.True(AreEqual(expectedResult, result.Values[0]));
        }

        [Theory]
        [InlineData("[]", new int[] {})]
        [InlineData("[1]", new[] { 1 })]
        [InlineData("[1, 2]", new[] { 1, 2 })]
        [InlineData("[2, 1]", new[] { 1, 2 })]
        [InlineData("[3, 22, 111]", new[] { 3, 22, 111 })]
        public void TestSortIntsNoComparer(string array, int[] expectedResult)
        {
            TestSort(array, expectedResult.Cast<object>().ToArray());
        }

        [Theory]
        [InlineData("[]", new int[] {})]
        [InlineData("[1]", new[] { 1 })]
        [InlineData("[1, 2]", new[] { 2, 1 })]
        [InlineData("[2, 1]", new[] { 2, 1 })]
        [InlineData("[3, 22, 111]", new[] { 111, 22, 3 })]
        public void TestSortIntsDesc(string array, int[] expectedResult)
        {
            TestSort(array, expectedResult.Cast<object>().ToArray(), cmpFunc: "(a, b) => a < b ? 1 : a === b ? 0 : -1");
        }

        [Theory]
        [InlineData("[]", new string[] {})]
        [InlineData("['1']", new[] { "1" })]
        [InlineData("['1', '2']", new[] { "1", "2" })]
        [InlineData("['2', '1']", new[] { "1", "2" })]
        [InlineData("['3', '22', '111']", new[] { "111", "22", "3" })]
        [InlineData("['a', 'aa']", new[] { "a", "aa" })]
        [InlineData("['aa', 'a']", new[] { "a", "aa" })]
        [InlineData("['bb', 'a', 'aaa', 'aa']", new[] { "a", "aa", "aaa", "bb" })]
        public void TestSortStringsNoComparer(string array, string[] expectedResult)
        {
            TestSort(array, expectedResult.Cast<object>().ToArray());
        }

        [Theory]
        [InlineData("[]", new string[] { })]
        [InlineData("['1']", new[] { "1" })]
        [InlineData("['3', '22', '111']", new[] { "3", "22", "111" })]
        public void TestSortStringsByLength(string array, string[] expectedResult)
        {
            TestSort(array, expectedResult.Cast<object>().ToArray(), "(a: string, b: string) => a.length < b.length ? -1 : 1");
        }

        [Theory]
        [InlineData("[{}, {a: 1}]", "Expecting type(s) 'number or string'")]
        [InlineData("[[], {a: 1}]", "Expecting type(s) 'number or string'")]
        [InlineData("[1, '2']", "Expecting type(s) 'number'", "but got '2' of type 'string'")]
        [InlineData("['1', 2]", "Expecting type(s) 'string'", "but got '2' of type 'number'")]
        public void TestSortFailUnexpectedType(string array, string expectedError1, string expectedError2 = "")
        {
            string spec = I($"namespace M {{ export const x = {array}.sort(); }}");
            TestResult result = EvaluateSpec(spec, new[] { "M.x" });
            result.ExpectErrorMessageSubstrings(new[] { expectedError1, expectedError2 });
        }

        [Theory]
        [InlineData("[1, 2]", "(a, b) => [1, 2][123]", "Index 123 is outside the bounds of the array '[1, 2]' ")]
        public void TestSortFailWhenBogusUserComparer(string array, string func, string expectedError)
        {
            string spec = I($"namespace M {{ export const x = {array}.sort({func}); }}");
            TestResult result = EvaluateSpec(spec, new[] { "M.x" });
            result.ExpectErrorMessageSubstrings(new[] { expectedError });
        }

        private void TestSort(string array, object[] expectedResult, string cmpFunc = "")
        {
            string spec = I($"namespace M {{ export const x = {array}.sort({cmpFunc}); }}");
            TestResult result = EvaluateSpec(spec, new[] { "M.x" });
            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.True(AreEqual(expectedResult, result.Values[0]));
        }

        [Theory]
        [InlineData("[]", new object[] {})]
        [InlineData("[1, 2, 3, 4, 5]", new object[] { 5, 4, 3, 2, 1 })]
        public void TestReverse(string array, object[] expectedResult)
        {
            CheckResult(EvaluateSpec($@"
namespace M {{
    export const x = {array}.reverse();
}}
", new[] { "M.x" }), expectedResult);
        }

        [Theory]
        [InlineData(0, 3, 1, new int[] { 0, 1, 2, 3 })]
        [InlineData(0, 3, 2, new int[] { 0, 2, 3 })]
        [InlineData(0, 3, 3, new int[] { 0, 3 })]
        [InlineData(1, 3, 1, new int[] { 1, 2, 3 })]
        [InlineData(1, 3, 2, new int[] { 1, 3 })]
        [InlineData(1, 3, 3, new int[] { 1, 3 })]
        [InlineData(1, 1, 1, new int[] { 1 })]
        [InlineData(10, 3, 3, new int[] { })]
        public void TestRange(int start, int stop, int step, int[] expectedResult)
        {
            // If step is 1 then run the test 3 times: (1) no step is specified, (2) step is undefined, and (3) step is 1.
            // In all cases the expected result is the same.
            string[] stepChoices = step == 1
                ? new[] { string.Empty, ", 1", ", undefined" }
                : new[] { $", {step}" };

            foreach (var optionalStepArgument in stepChoices)
            {
                CheckResult(EvaluateSpec($@"
namespace M {{
    export const x = Array.range({start}, {stop}{optionalStepArgument});
}}
", new[] { "M.x" }), expectedResult);
            }
        }

        private void CheckResult(TestResult result, object expectedResult, int errorCount = 0)
        {
            Contract.Requires(errorCount >= 0);
            if (errorCount > 0)
            {
                result.ExpectErrors(count: errorCount);
            }
            else
            {
                result.ExpectNoError();
            }

            result.ExpectValues(count: 1);
            Assert.True(AreEqual(expectedResult, result.Values[0]), "Not equal, got: [" +string.Join(",", result.Values[0]) + "]");
        }
    }
}
