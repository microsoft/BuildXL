// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretLocals : DsTest
    {
        public InterpretLocals(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void NamespaceWithTwoLocalsShoulFailWithDuplicate()
        {
            string code = @"
const x = 1;
const x = 42;
";

            var result = ParseWithDiagnosticId(code, LogEventId.TypeScriptBindingError);
            Assert.Contains("Cannot redeclare block-scoped variable 'x'", result.Message);
        }

        [Theory]
        [InlineData(@"
const x = 1;
function foo() {x = 1;}")]
        [InlineData(@"
function foo() {
  const x = 1;
  x = 1;
}")]
        [InlineData(@"

function foo() {
  const x = 1;
  const fn = () => {x = 1;};
}")]
        [InlineData(@"
const x = () => {
  const x = 1;
  x = 1;
};")]
        public void TestInvalidAssignment(string code)
        {
            ParseWithDiagnosticId(code, LogEventId.LeftHandSideOfAssignmentExpressionCannotBeAConstant);
        }

        [Theory]
        [InlineData(@"
const x = 1;
function foo() {x++;}")]
        [InlineData(@"
function foo() {
  const x = 1;
  x++;
}")]
        [InlineData(@"

function foo() {
  const x = 1;
  const fn = () => {x++;};
}")]
        [InlineData(@"
const x = () => {
  const x = 1;
  x++;
};")]
        public void TestInvalidIncrement(string code)
        {
            ParseWithDiagnosticId(code, LogEventId.TheOperandOfAnIncrementOrDecrementOperatorCannotBeAConstant);
        }

        [Fact]
        public void TopLevelVariableCannotBeUsedBeforeDeclared()
        {
            string code = @"
const x = y;
const y = 42;
";
            var result = ParseWithDiagnosticId(code, LogEventId.BlockScopedVariableUsedBeforeDeclaration);
            Assert.Equal((int)LogEventId.BlockScopedVariableUsedBeforeDeclaration, result.ErrorCode);
        }

        [Fact]
        public void TopLevelVariableCannotBeUsedInFunctionBodyBeforeDeclared()
        {
            string code = @"
function f() {
    let x = y;
    return x;
}
const y = 42;
";
            ParseWithDiagnosticId(code, LogEventId.BlockScopedVariableUsedBeforeDeclaration);
        }

        [Fact(Skip = "This should be a special error message!!")]
        public void TopLevelVariableCannotBeUsedInArrowFunctionBodyBeforeDeclared()
        {
            string code = @"
const y = 42;

const f = () => {
    let x = y;
    y = 5;
    return x;
};
";
            ParseWithDiagnosticId(code, LogEventId.BlockScopedVariableUsedBeforeDeclaration);
        }

        [Fact]
        public void TopLevelVariableCannotBeUsedInLambdaBodyBeforeDeclared()
        {
            string code = @"
const f = () => {
    let x = y;
    return x;
};
const y = 42;
";
            ParseWithDiagnosticId(code, LogEventId.BlockScopedVariableUsedBeforeDeclaration);
        }

        [Fact]
        public void NamespaceLevelVariableCannotBeUsedBeforeDeclared()
        {
            string code = @"
export namespace A.B {
   export const z = w;
   const w = 0;
}

export const r = A.B.z;
";

            ParseWithDiagnosticId(code, LogEventId.BlockScopedVariableUsedBeforeDeclaration);
        }

        [Fact]
        public void LocalConstVariableCannotBeUsedBeforeDeclared()
        {
            string code = @"
function foo() {
  const x = y;
  const y = 42;
}";

            ParseWithDiagnosticId(code, LogEventId.BlockScopedVariableUsedBeforeDeclaration);
        }

        [Fact]
        public void LocalLetVariableCannotBeUsedBeforeDeclared()
        {
            string code = @"
function foo() {
  const x = y;
  const y = 42;
}";

            ParseWithDiagnosticId(code, LogEventId.BlockScopedVariableUsedBeforeDeclaration);
        }

        [Fact]
        public void InterpretLocalDeclarationWithTwoVariablesInGlobalScope()
        {
            string code = @"
namespace M {
    export const r1: number = 1, r2: string = ""42"";
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(1, result["M.r1"]);
            Assert.Equal("42", result["M.r2"]);
        }

        [Fact]
        public void LocalWithSimilarNameShouldFail()
        {
            // Note, this should not fail on vars, but vars are disabled in DScript!
            string code = @"
function foo() {
    const x = 0;
    const x = ""foo"";
}";
            
            var result = ParseWithDiagnosticId(code, LogEventId.TypeScriptBindingError);
            Assert.Contains("Cannot redeclare block-scoped variable 'x'", result.Message);
        }

        [Fact]
        public void LocalWithSimilarNameAsArgumentShouldFail()
        {
            string code = @"
function foo(x: number) {
    const x = ""foo"";
}";

            var result = ParseWithDiagnosticId(code, LogEventId.TypeScriptBindingError);
            Assert.Contains("Duplicate identifier 'x'", result.Message);
        }

        [Fact]
        public void FunctionCouldHaveLocalsTheSameAsArgumentsInNestedScope()
        {
            // Local could be declared in the nested scope!
            string code = @"
function foo(x: number) {
    {
         let x = ""42"";
         return x;
    }
}
const r = foo(42); //""42""
";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal("42", result);
        }

        [Fact]
        public void EvaluateDifferentLocalsSuccessfully()
        {
            string code = @"
function foo() {return 42;}
export const z = foo();
export const z2 = z;

export namespace A.B {
  const w = 42;   
  export const z = w;
  export function getZ() {return z;}
}

const b = A.B;
export const r2 = b.getZ();

export const r = A.B.getZ();
";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(42, result);
        }

        [Fact]
        public void FunctionWithTwoSimilarArgumentsShouldFail()
        {
            string code = @"
function foo(x: number, x: string) {
}";
            
            var result = ParseWithDiagnosticId(code, LogEventId.TypeScriptBindingError);
            Assert.Contains("Duplicate identifier 'x'", result.Message);
        }

        [Fact]
        public void InterpretConstBindingOnNamespaceLevel()
        {
            // Const doesn't have any additional semantic!
            string code = @"
namespace M {
    export const r1: number = 1, r2: string = ""42"";
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(1, result["M.r1"]);
            Assert.Equal("42", result["M.r2"]);
        }

        [Fact]
        public void InterpretConstBindingOnFunctionLevel()
        {
            string code = @"
namespace M {
    function foo() {
      const x = 42;
      return x;
    }

    export const r = foo(); //42
}";
            var result = EvaluateExpressionWithNoErrors(code, "M.r");

            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretLocalDeclarationWithTwoVariablesInFunctionScope()
        {
            string code = @"
namespace M {
    function foo() {
       let r1: number = 1, r2: string = ""42"";
       return [r1, r2];
    }

    const r = foo();
    export const r1 = r[0], r2 = r[1];
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(1, result["M.r1"]);
            Assert.Equal("42", result["M.r2"]);
        }

        // TODO:ST: test case - fail on mutating vars in global scope
        // TODO:ST: test case - fail on var/let on global scope

        [Fact]
        public void EvaluateNamespaceMemberAccess()
        {
            string spec =
@"namespace M
{
    export const x = 42;
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(42, result);
        }

        [Fact]
        public void EvaluateUndefined()
        {
            string spec =
@"namespace M
{
    export const x: string = undefined;
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(UndefinedValue.Instance, result);
        }

        [Fact]
        public void EvaluateNumericPlus()
        {
            string spec =
@"namespace M
{
    const y = 41;
    export const x = y + 1;
}";
            var result = EvaluateExpressionWithNoErrors(spec, "M.x");
            Assert.Equal(42, result);
        }
    }
}
