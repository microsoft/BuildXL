// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Values;
using Test.DScript.Ast.DScriptV2;
using TypeScript.Net.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestEnumMemberInitializers : SemanticBasedTests
    {
        public TestEnumMemberInitializers(ITestOutputHelper output) : base(output) { }

        private class EnumEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y)
            {
                var number = Convert.ToInt32(x);
                var @enum = (EnumValue)y;
                return number == @enum.Value;
            }

            public int GetHashCode(object obj)
            {
                throw new NotImplementedException();
            }
        }

        private static readonly EnumEqualityComparer s_enumComparer = new EnumEqualityComparer();

        [Fact]
        public void EnumValuesWithExpresssionsOnRhs()
        {
            string spec = @"
namespace M {
    export const enum Color {
        value1 = 1,
        value2 = 2,
        value3 = value1 | value2,
        value4 = value1 ^ value2,
        value5 = value1 & value2,
        value6 = value1 + value2,
        value7 = value2 - value1,
        value8 = value1 * value2,
        value10 = value1 % value2,
        value11 = value1 << value2,
        value12 = value1 >> value2,
        value13 = value1 >>> value2,
        value14 = +value1,
        value15 = -(-value1),
        value16 = ~(~value1),
        value17 = (value1),
        value18 = value1,
        value19 = value1 | (value2 & (~value1)) // 1 | (2 & ~1) = 1 | (2 & -2) = 1 | 2 = 3
    }

    export const f1 = Color.value1;
    export const f2 = Color.value2;
    export const f3 = Color.value3;
    export const f4 = Color.value4;
    export const f5 = Color.value5;
    export const f6 = Color.value6;
    export const f7 = Color.value7;
    export const f8 = Color.value8;
    export const f10 = Color.value10;
    export const f11 = Color.value11;
    export const f12 = Color.value12;
    export const f13 = Color.value13;
    export const f14 = Color.value14;
    export const f15 = Color.value15;
    export const f16 = Color.value16;
    export const f17 = Color.value17;
    export const f18 = Color.value18;
    export const f19 = Color.value19;
}";

            var result = BuildWithPrelude()
                .AddSpec(spec)
                .EvaluateExpressionsWithNoErrors(
                    "M.f1",
                    "M.f2",
                    "M.f3",
                    "M.f4",
                    "M.f5",
                    "M.f6",
                    "M.f7",
                    "M.f8",
                    "M.f10",
                    "M.f11",
                    "M.f12",
                    "M.f13",
                    "M.f14",
                    "M.f15",
                    "M.f16",
                    "M.f17",
                    "M.f18",
                    "M.f19");

            Assert.Equal(1, result["M.f1"], s_enumComparer);
            Assert.Equal(2, result["M.f2"], s_enumComparer);
            Assert.Equal(3, result["M.f3"], s_enumComparer);
            Assert.Equal(3, result["M.f4"], s_enumComparer);
            Assert.Equal(0, result["M.f5"], s_enumComparer);
            Assert.Equal(3, result["M.f6"], s_enumComparer);
            Assert.Equal(1, result["M.f7"], s_enumComparer);
            Assert.Equal(2, result["M.f8"], s_enumComparer);
            Assert.Equal(1, result["M.f10"], s_enumComparer);
            Assert.Equal(4, result["M.f11"], s_enumComparer);
            Assert.Equal(0, result["M.f12"], s_enumComparer);
            Assert.Equal(0, result["M.f13"], s_enumComparer);
            Assert.Equal(1, result["M.f14"], s_enumComparer);
            Assert.Equal(1, result["M.f15"], s_enumComparer);
            Assert.Equal(1, result["M.f16"], s_enumComparer);
            Assert.Equal(1, result["M.f17"], s_enumComparer);
            Assert.Equal(1, result["M.f18"], s_enumComparer);
            Assert.Equal(3, result["M.f19"], s_enumComparer);
        }

        [Fact]
        public void InvalidEnumValuesWithExpresssionsOnRhs()
        {
            string spec = @"
namespace M {
    export const x = 4;

    export const enum Color {
        value1 = x
    }

    export const f1 = Color.value1;
}";

            var result = BuildWithPrelude()
                .AddSpec(spec)
                .EvaluateWithFirstError("M.f1");

            Assert.Contains(Errors.In_const_enum_declarations_member_initializer_must_be_constant_expression.Message, result.Message);
            Assert.Equal(6, result.Location.Value.Line);
        }
    }
}
