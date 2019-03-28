// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using TypeScript.Net.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    /// <summary>
    /// Set of integration tests for evaluating enums.
    /// </summary>
    /// <remarks>
    /// Currently DScript doesn't support runtime behavior for type casting, that's why every test case
    /// has explicit cast from enum member to a number or use custom equality comparer.
    /// </remarks>
    public sealed class InterpretEnums : DsTest
    {
        private class EnumEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y)
            {
                var number = Convert.ToInt32(x);
                var @enum = (EnumValue) y;
                return number == @enum.Value;
            }

            public int GetHashCode(object obj)
            {
                throw new NotImplementedException();
            }
        }

        private static readonly EnumEqualityComparer s_enumComparer = new EnumEqualityComparer();

        public InterpretEnums(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void ValueOfReturnsTheUnderlyingValue()
        {
            string spec = @"
const enum Enum1 { value1 = 42 }
export const value = Enum1.value1.valueOf();
export const strValue = Enum1.value1.valueOf().toString(16);
";

            var result = EvaluateExpressionsWithNoErrors(spec, "value", "strValue");

            Assert.Equal(42, result["value"]);
            Assert.Equal("2a", result["strValue"]);
        }

        [Fact(Skip = "Only numbers are allowed in enum initializers so far. Need to implement this!")]
        public void CrossEnumInitializer()
        {
            // Currently, DScript doesn't support initializers that are not numbers.
            string spec = @"
namespace M {
    const enum Enum1 {
        value1 = 1,
    }

    const enum Enum2 {
       value1 = Enum1.value1,
    }

    export const v1 = Enum2.value1;
}";

            var result = EvaluateExpressionWithNoErrors(spec, "M.v1");

            Assert.Equal(1, result, s_enumComparer);
        }

        [Fact(Skip = "Only numbers are allowed in enum initializers so far. Need to implement this!")]
        public void InnerEnumInitializer()
        {
            // Currently, DScript doesn't support initializers that are not numbers.
            string spec = @"
namespace M {
    const enum Enum2 {
       value1 = 1 << 0,
       value2 = 1 << 1,
       value3 = value1 | value2
    }

    export const v1 = Enum2.value1;
}";

            var result = EvaluateExpressionWithNoErrors(spec, "M.v1");

            Assert.Equal(1, result, s_enumComparer);
        }

        [Fact]
        public void EnumsWithDifferentLiteralTypes()
        {
            // TypeScript/DScript supports decimal, octal, hexademical and binary literals
            string spec = @"
namespace M {
    const enum Fruit {
        value1 = 0b01,
        value2 = 0o02,
        value3 = 3,
        value4 = 0x04,
    }

    export const f1 = <number>Fruit.value1;
    export const f2 = <number>Fruit.value2;
    export const f3 = <number>Fruit.value3;
    export const f4 = <number>Fruit.value4;
}";
            var result = EvaluateExpressionsWithNoErrors(spec, "M.f1", "M.f2", "M.f3", "M.f4");

            // Type casting doesn't working right now, so still need to use comparer!
            Assert.Equal(1, result["M.f1"], s_enumComparer);
            Assert.Equal(2, result["M.f2"], s_enumComparer);
            Assert.Equal(3, result["M.f3"], s_enumComparer);
            Assert.Equal(4, result["M.f4"], s_enumComparer);
        }

        [Fact]
        public void EnumValuesWithConstExpressions()
        {
            // TODO: because interpeter doesn
            string spec = @"
export namespace M {
    const enum Fruit {
        value1 = 1 << 2,
        value2 = 0xA1,
        value3 = 1 + 3,
        value4 = 5*3,
    }

    export const f1 = Fruit.value1; // 4
    export const f2 = Fruit.value2; // 161
    export const f3 = Fruit.value3;
    export const f4 = Fruit.value4;
}";

            var result = EvaluateExpressionsWithNoErrors(spec, "M.f1", "M.f2", "M.f3", "M.f4");
            Assert.Equal(4, result["M.f1"], s_enumComparer);
            Assert.Equal(161, result["M.f2"], s_enumComparer);
            Assert.Equal(4, result["M.f3"], s_enumComparer);
            Assert.Equal(15, result["M.f4"], s_enumComparer);
        }

        [Fact]
        public void EnumValuesWithOrExpressions()
        {
            string spec = @"
namespace M {
    const enum Fruit {
        value1 = 1 << 0,
        value2 = 1 << 1,
        value3 = 1 << 2,
        value4 = 1 << 3,
        value5 = (1 << 2) | (1 << 3),
    }

    export const f1 = Fruit.value1;
    export const f2 = Fruit.value2;
    export const f3 = Fruit.value3;
    export const f4 = Fruit.value4;
    export const f5 = Fruit.value5;
}";
            var result = EvaluateExpressionsWithNoErrors(spec, "M.f1", "M.f2", "M.f3", "M.f4", "M.f5");

            Assert.Equal(1, result["M.f1"], s_enumComparer);
            Assert.Equal(2, result["M.f2"], s_enumComparer);
            Assert.Equal(4, result["M.f3"], s_enumComparer);
            Assert.Equal(8, result["M.f4"], s_enumComparer);
            Assert.Equal(12, result["M.f5"], s_enumComparer);
        }

        [Fact]
        public void EnumValuesWithMissedInitializers()
        {
            string spec = @"
namespace M {
    const enum Fruit {
        value1,
        value2 = 41,
        value3,
    }

    export const f1 = Fruit.value1;
    export const f2 = Fruit.value2;
    export const f3 = Fruit.value3;
}";
            var result = EvaluateExpressionsWithNoErrors(spec, "M.f1", "M.f2", "M.f3");

            Assert.Equal(0, result["M.f1"], s_enumComparer);
            Assert.Equal(41, result["M.f2"], s_enumComparer);
            Assert.Equal(42, result["M.f3"], s_enumComparer);
        }

        [Fact]
        public void TestEnumValues()
        {
            string spec = @"
namespace M 
{
    const enum Fruit
    {
        mango = 1,
        banana = 2,
        papaya = 4,
        apple = 8
    }

    export const x = Fruit.papaya.toString();
    const y = Fruit.mango | Fruit.banana | Fruit.apple;
    const w = Fruit.mango ^ Fruit.banana;
    export const z = (y & Fruit.banana) === Fruit.banana;
    export const t = (y & w) === w;
    export const m = y.hasFlag(Fruit.banana | Fruit.apple);
    export const n = ~~Fruit.banana === Fruit.banana;
}";
            var result = EvaluateExpressionsWithNoErrors(spec, "M.x", "M.z", "M.t", "M.m", "M.n");

            Assert.Equal("papaya", result["M.x"]);
            Assert.Equal(true, result["M.z"]);
            Assert.Equal(true, result["M.t"]);
            Assert.Equal(true, result["M.m"]);
            Assert.Equal(true, result["M.n"]);
        }

        [Fact]
        public void TestInvalidEnumMemberNonNumericExpression()
        {
            string spec = @"
namespace M 
{
    const enum Fruit
    {
        mango = true,
        banana = false
    }

    export const x = Fruit.mango;
}";
            EvaluateWithTypeCheckerDiagnostic(spec, Errors.In_const_enum_declarations_member_initializer_must_be_constant_expression);
        }
    }
}
