// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public class TestSemanticBasedEvaluation : SemanticBasedTests
    {
        public TestSemanticBasedEvaluation(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void InterpetLocalFunctionInvocation()
        {
            string code =
@"namespace X {
  function foo() {return 42;}

  export const r = foo();
}";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("X.r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpetLocalFunctionShouldFailIfNotResolved()
        {
            string code =
@"function foo() {return 42;}

export const r = foo2();";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateWithFirstError();
            Assert.Equal(11231, result.ErrorCode);
        }

        [Fact]
        public void InterpretFileBasedBinding()
        {
            string code =
@"const x = 42;
export const r = x;";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretEnumBasedBinding()
        {
            string code =
@"export const enum X {value = 42}
export const r = X.value.toString();";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal("value", result);
        }

        [Fact]
        public void InterpretEnumInNestedNamespaceBasedBinding()
        {
            string code =
@"export namespace A.B.C { export const enum X {value = 42} }
const x = A.B.C;
export const r1 = A.B.C.X.value.toString();
export const r2 = x.X.value.toString();";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal("value", result["r1"]);
            Assert.Equal("value", result["r2"]);
        }

        [Fact]
        public void InterpretShorthandPropertyAssignment()
        {
            string code =
@"function foo() { return 42; }
const z = 41;
const x = { foo, z, bar: foo };
export const r1 = x.foo(); // 42
export const r2 = x.z; // 41
export const r3 = x.bar(); // 42";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2", "r3");
            Assert.Equal(42, result["r1"]);
            Assert.Equal(41, result["r2"]);
            Assert.Equal(42, result["r3"]);
        }
        
        [Fact]
        public void InterpretObjectLiteralWithFunctionInFunctionArguments()
        {
            string code =
@"function foo() { return 42; }
function capturesArgumentFunction(x: () => number) {
    return { x };
}
export const r = capturesArgumentFunction(foo).x();";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretObjectLiteralWithConstantInFunctionArguments()
        {
            string code =
@"function capturesArgument(x: number) {
    return { x, y: x };
}

const temp = capturesArgument(42);
export const r1 = temp.x;
export const r2 = temp.y;
";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal(42, result["r1"]);
            Assert.Equal(42, result["r2"]);
        }

        [Fact]
        public void InterpretObjectLiteralWithNestedLiterals()
        {
            string code =
@"export namespace Bar {
    // Nested crazyness
    function foo(n: number) { return n; }
    const ol1 = { foo };
    export const ol2 = { ol1 };
    export const tmp = ol2.ol1.foo(42);
}
export const r1 = Bar.tmp;
export const r2 = Bar.ol2.ol1.foo(42);
";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal(42, result["r1"]);
            Assert.Equal(42, result["r2"]);
        }

        [Fact]
        public void InterpretNamespaceBindingWithConstant()
        {
            string code =
@"namespace Foo {
          export const x = {r: 42};
        }

        export const r = Foo.x.r;";

            // Note, in this case r will point to const expression!
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretNamespaceBindingWithExplicitInterface()
        {
            string code =
@"namespace Foo {
   export interface Bar {
     x: number;
   }
   
   export const x: Bar = {x: 42};
}

const x = Foo;
const tmp = {x: x.x.x};
export const r = tmp.x;";

            // Note, in this case r will point to const expression!
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal(42, result);
        }

        [Fact(Skip = "Need to merge some stuff before enabling it.")]
        public void InterpretNamespaceBindingWithExplicitInterfaceAndTypeof()
        {
            string code =
@"namespace Foo {
   export interface Bar {
     x: number;
   }
   
   export const x: Bar = {x: 42};
}

const x = Foo.Bar;
const tmp: typeof x = {x: Foo.x.x};
export const r = tmp.x;";

            // Note, in this case r will point to const expression!
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretNamespaceBindingWithFunctionInvocation()
        {
            string code =
@"namespace Foo {
            export function foo() {return 42;}
            export const x = {foo};
        }

        export const r = Foo.x.foo();";

            // Note, in this case r will point to const expression!
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void MethodGroupConversion()
        {
            string code =
@"function foo() {return 42;}

        const f = foo;
        export const r = f();";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal(42, result);
        }        
    }
}
