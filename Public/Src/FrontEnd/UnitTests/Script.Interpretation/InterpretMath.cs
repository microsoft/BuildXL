// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    /// <summary>
    /// Set of tests for ambient Math namespace.
    /// </summary>
    public sealed class InterpretMath : DsTest
    {
        public InterpretMath(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(1, false)]
        [InlineData(-1, false)]
        [InlineData(int.MaxValue, false)]
        [InlineData(int.MinValue, true)]
        public void TestAbs(int input, bool overflow)
        {
            string code =
@"export const r = Math.abs(" + input + ");";

            if (overflow)
            {
                var result = Build().AddSpec(code).EvaluateWithFirstError("r");

                EventIdEqual(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.ArithmeticOverflow, result.ErrorCode);
            }
            else
            {
                var result = Build().AddSpec(code).EvaluateExpressionWithNoErrors("r");
                Assert.Equal((int)Math.Abs((long)input), result);
            }
        }

        [Theory]
        [InlineData(false, new int[0])]
        [InlineData(false, new[]{1})]
        [InlineData(false, new[]{int.MinValue, 1})]
        [InlineData(true, new[]{int.MaxValue, 1})]
        [InlineData(false, new[] {1, 2, 3})]
        public void TestSum(bool overflow, params int[] values)
        {
            string code =
                @"export const r = Math.sum(" + string.Join(", ", values) + ");";
            if (overflow)
            {
                var result = Build().AddSpec(code).EvaluateWithFirstError("r");

                EventIdEqual(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.ArithmeticOverflow, result.ErrorCode);
            }
            else
            {
                var result = Build().AddSpec(code).EvaluateExpressionWithNoErrors("r");
                Assert.Equal(values.Sum(), result);
            }
        }
        
        [Theory]
        [InlineData(new[]{1})]
        [InlineData(new[]{int.MinValue, 1 })]
        [InlineData(new[]{int.MaxValue, 1 })]
        [InlineData(new[]{1, 2, 3 })]
        public void TestMax(params int[] values)
        {
            string code =
                @"export const r = Math.max(" + string.Join(", ", values) + ");";
            var result = Build().AddSpec(code).EvaluateExpressionWithNoErrors("r");

            Assert.Equal(values.Max(), result);
        }

        [Fact]
        public void MaxOnEmptyListIsUndefined()
        {
            string code =
                @"export const r = Math.max();";
            var result = Build().AddSpec(code).EvaluateExpressionWithNoErrors("r");

            Assert.Equal(UndefinedValue.Instance, result);
        }

        [Theory]
        [InlineData(new []{1})]
        [InlineData(new []{int.MinValue, 1})]
        [InlineData(new []{int.MaxValue, 1})]
        [InlineData(new []{1, 2, 3})]
        public void TestMin(params int[] values)
        {
            string code =
                @"export const r = Math.min(" + string.Join(", ", values) + ");";
            var result = Build().AddSpec(code).EvaluateExpressionWithNoErrors("r");

            Assert.Equal(values.Min(), result);
        }

        [Fact]
        public void MinOnEmptyListIsUndefined()
        {
            string code =
                @"export const r = Math.min();";
            var result = Build().AddSpec(code).EvaluateExpressionWithNoErrors("r");

            Assert.Equal(UndefinedValue.Instance, result);
        }

        [Theory]
        [InlineData(1, 0, false)]
        [InlineData(-1, 0, false)]
        [InlineData(int.MaxValue, int.MaxValue, true)]
        [InlineData(7, 8, false)]
        public void TestPow(int @base, int exponent, bool overflow)
        {
            string code =
                @"export const r = Math.pow(" + @base + ", " + exponent + ");";

            if (overflow)
            {
                var result = Build().AddSpec(code).EvaluateWithFirstError("r");

                EventIdEqual(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.ArithmeticOverflow, result.ErrorCode);
            }
            else
            {
                var result = Build().AddSpec(code).EvaluateExpressionWithNoErrors("r");
                Assert.Equal((int)Math.Pow((int)@base, (int)exponent), result);
            }
        }

        [Theory]
        [InlineData(1, 0, true)]
        [InlineData(-1, 0, true)]
        [InlineData(int.MaxValue, 42, false)]
        [InlineData(7, 8, false)]
        public void TestMod(int divident, int divisor, bool divideByZero)
        {
            string code =
                @"export const r = Math.mod(" + divident + ", " + divisor + ");";

            if (divideByZero)
            {
                var result = Build().AddSpec(code).EvaluateWithFirstError("r");

                EventIdEqual(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.DivideByZero, result.ErrorCode);
            }
            else
            {
                var result = Build().AddSpec(code).EvaluateExpressionWithNoErrors("r");
                Assert.Equal(divident % divisor, result);
            }
        }

        [Theory]
        [InlineData(5, 6, false)]
        [InlineData(123121, 121, false)]
        [InlineData(123121, 0, true)]
        [InlineData(int.MaxValue, 42, false)]
        [InlineData(7, 8, false)]
        public void TestDiv(int divident, int divisor, bool divideByZero)
        {
            string code =
                @"export const r = Math.div(" + divident + ", " + divisor + ");";

            if (divideByZero)
            {
                var result = Build().AddSpec(code).EvaluateWithFirstError("r");

                EventIdEqual(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.DivideByZero, result.ErrorCode);
            }
            else
            {
                var result = Build().AddSpec(code).EvaluateExpressionWithNoErrors("r");
                Assert.Equal(divident / divisor, result);
            }
        }
    }
}
