// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretArrays : DsTest
    {
        public InterpretArrays(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void EvaluateLargeArrayInParallel()
        {
            // Array with such a huge number of elements would be evaluated in parallel.
            // This test could be used to sanity check that parallel evaluation is successful.
            const string Spec = @"
function func(x: number) {
  return [x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x,x];
}

export const res = func(200).length;";
            var result = EvaluateExpressionWithNoErrors(Spec, "res");
            Assert.Equal(455, result);
        }

        [Fact]
        public void EvaluateArrayWithEmptyElement()
        {
            // Bug600223
            const string Spec = @"function func() { return [1,,2]; }
export const x = func().length; // should be 3
export const y = func()[1]; // should be undefined";
            var result = EvaluateExpressionsWithNoErrors(Spec, "x", "y");
            Assert.Equal(3, result["x"]);
            Assert.Equal(UndefinedValue.Instance, result["y"]);
        }

        [Fact]
        public void ArrayLiteralWithSpreadShouldNotCrashIfTheTypeIsWrong()
        {
            // Bug998904
            const string Spec = @"
export const x = [1,2, ...(<any>4)];";
            var result = EvaluateWithFirstError(Spec);

            Assert.Equal(LogEventId.UnexpectedValueType, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void EvaluateArrayLength()
        {
            const string Spec = @"
namespace M
{
    function func(x: number) { return [x, 5]; }
    export const x = [1, ...[2, 3], ...func(42), 7].length;
}";
            var result = EvaluateExpressionWithNoErrors(Spec, "M.x");
            Assert.Equal(6, result);
        }

        [Fact]
        public void FilterEvenNumbers()
        {
            const string Spec = @"
export const x = [0,1,2,3,4,5,6,7,8].filter(x => (x % 2) === 0).length;
";
            var result = EvaluateExpressionWithNoErrors(Spec, "x");
            Assert.Equal(5, result);
        }

        [Fact]
        public void FilterNumbersGreaterThan5()
        {
            const string Spec = @"
export const x = [1,2,3,4,5,6,7,8].filter(x => x > 5).length;
";
            var result = EvaluateExpressionWithNoErrors(Spec, "x");
            Assert.Equal(3, result);
        }

        [Fact]
        public void FilterUndefines()
        {
            const string Spec = @"
export const x = [undefined,1,2,3,undefined,4,5,undefined, undefined, 6, 7].filter(x => x !== undefined).length;
";
            var result = EvaluateExpressionWithNoErrors(Spec, "x");
            Assert.Equal(7, result);
        }

        [Fact]
        public void FilterNoneUndefines()
        {
            const string Spec = @"
export const x = [1,2,3,4,5].filter(x => x !== undefined).length;
";
            var result = EvaluateExpressionWithNoErrors(Spec, "x");
            Assert.Equal(5, result);
        }

        [Fact]
        public void FilterWithMapEnumConst()
        {
            const string Spec = @"
export function mapEnumConst(e: any, ...mapping: [any, string][]): string {
    if (e === undefined) return undefined;
    let matches = mapping.filter(kvp => kvp[0] === e);
    if (matches.length === 0) return undefined;
    return matches[0][1];
}

export const enum Platform {x86, x64};
const hostPlatform = Platform.x86;
export const hostArch: string = mapEnumConst(hostPlatform, 
        [Platform.x86, 'x86'],
        [Platform.x64, 'amd64']
    );";
            var result = EvaluateExpressionWithNoErrors(Spec, "hostArch");
            Assert.Equal("x86", result);
        }

        [Fact]
        public void FilterAllUndefines()
        {
            const string Spec = @"
export const x = [undefined,undefined,undefined].filter(x => x !== undefined).length;
";
            var result = EvaluateExpressionWithNoErrors(Spec, "x");
            Assert.Equal(0, result);
        }

        [Fact]
        public void SpreadOperatorShouldHaveLowestPresedenceThanOrOperator()
        {
            // x should be 3, because ... has lowest priority,
            // so this code is equivalent to: [...(a || [1, 2], 42].length
            const string Code = @"
const a = undefined;
export const x = [...a || [1, 2], 42].length;";

            var result = EvaluateExpressionWithNoErrors(Code, "x");
            Assert.Equal(3, result);
        }

        [Fact]
        public void EvaluateArrayLengthAfterCallingConcat()
        {
            const string Spec = @"
namespace M
{
    const a1 = [1, 2];
    const a2 = [3, 4];

    export const x = a1.concat(a2).length;
}";
            var result = EvaluateExpressionWithNoErrors(Spec, "M.x");
            Assert.Equal(4, result);
        }

        [Fact]
        public void TestArraySpread()
        {
            const string Spec = @"
namespace M 
{
    const x = [1, 2, 3];
    const y = [4, 5, 6];
    export const z = [0, ...x, 7, 8, ...y, 9].length;
}";
            var result = EvaluateExpressionWithNoErrors(Spec, "M.z");
            Assert.Equal(10, result);
        }

        [Fact]
        public void TestZipWith()
        {
            const string Spec = @"
const a = [1,2,3];
const b = [4, 5];
export const r = a.zipWith(b, (l, r) => l + r); // [5, 7]";
            var result = EvaluateExpressionWithNoErrors(Spec, "r");
            
            CheckArray(new []{5, 7}, result);
        }

        [Fact]
        public void TestArrayAccess()
        {
            const string Spec = @"
namespace M 
{
    const x = [1, 2, 3];
    export const r1 = x[0];
    export const r2 = x[2];
}";
            var result = EvaluateExpressionsWithNoErrors(Spec, "M.r1", "M.r2");
            Assert.Equal(1, result["M.r1"]);
            Assert.Equal(3, result["M.r2"]);
        }

        [Fact]
        public void TestNonArrayMethodFromGeneratedArrayShouldFail()
        {
            // Bug #802180
            const string Spec = @"
import {Transformer} from 'Sdk.Transformers';

const x = Transformer.execute({
    tool: { exe: f`exec.exe` },
    workingDirectory: d`.`,
    arguments: [],
    implicitOutputs: [p`out1`, p`out2`]
});
export const y = (<{banana(): any}><any>x.getOutputFiles()).banana();
";
            EvaluateWithDiagnosticId(Spec, LogEventId.MissingInstanceMember);
        }
    }
}
