// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;
using static TypeScript.Net.Diagnostics.Errors;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestRuntimeErrorHandling : DsTest
    {
        public TestRuntimeErrorHandling(ITestOutputHelper output)
            : base(output)
        { }

        [Theory]
        [InlineData("[1,2]", "unknownProperty", LogEventId.MissingInstanceMember)]
        [InlineData("[1,2]", "unknownMethod()", LogEventId.MissingInstanceMember)]
        [InlineData("{}", "unknownMethod()", LogEventId.UnexpectedValueType)] //
        // Unknown properties on object literals are OK, because there is no way to distinguish missing property from optional property that was not provided
        // But it is fine to report a missing instance error when a function that doesn't exist tries to be applied.
        [InlineData("42", "unknownProperty", LogEventId.UnexpectedValueType)]
        [InlineData("42", "unknownProperty", LogEventId.UnexpectedValueType)]
        [InlineData("true", "unknownProperty", LogEventId.UnexpectedValueType)]
        [InlineData("false", "unknownProperty", LogEventId.UnexpectedValueType)]
        [InlineData("p`c:/foo/boo.cs`", "unknownProperty", LogEventId.UnexpectedValueType)]
        [InlineData("p`c:/foo/boo.cs`", "unknownMethod()", LogEventId.UnexpectedValueType)]
        [InlineData("f`c:/foo/boo.cs`", "unknownProperty", LogEventId.UnexpectedValueType)]
        [InlineData("f`c:/foo/boo.cs`", "unknownMethod()", LogEventId.UnexpectedValueType)]
        [InlineData("d`c:/foo`", "unknownProperty", LogEventId.UnexpectedValueType)]
        [InlineData("d`c:/foo`", "unknownMethod()", LogEventId.UnexpectedValueType)]
        public void TestUnresolvedInstanceMembers(string initializer, string member, LogEventId expectedError)
        {
            // Lets fix up the inline data for Unix runs
            if (initializer.Contains("`c:") && OperatingSystemHelper.IsUnixOS)
            {
                initializer = initializer.Replace("`c:", "`");
            }

            // Need to separate receiver and member access because bool.foo() fails at parse time
            // but const b: bool = true; b.foo() - at runtime.
            string code = I($"const l = {initializer}; const r = (<{{unknownProperty: any, unknownMethod(): any}}><any>l).{member};");
            var result = EvaluateWithFirstError(code);

            Assert.Equal(expectedError, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void TypeMismatchBetweenEmptyObjectLiteralAndString()
        {
            // At the evaluation time interpreter should return undefined.
            // This error should be catched by a checker.
            string code =
@"namespace X {
    export const r1 = (<{undefinedProperty: any}>{}).undefinedProperty; // undefined
}";
            var result = EvaluateExpressionsWithNoErrors(code, "X.r1");

            Assert.Equal(UndefinedValue.Instance, result["X.r1"]);
        }

        [Fact]
        public void TestErrorMessageForUnresolvedLocal()
        {
            string code =
@"namespace X {
    function foo(): number {
        return aaa;
    }

    export const r = foo();
}";
            EvaluateWithTypeCheckerDiagnostic(code, Cannot_find_name_0, "aaa");
        }

        [Fact]
        public void TestErrorMessageForAssigningToUnresolvedLocal()
        {
            // TypeScript error: Can't find name 'a'
            // DScript error: Left-hand side of an assignmetn must be a local variable
            // TODO: change the error message!
            string code =
@"namespace X {
    function foo(): number {
        aaa = 42;
        return 42;
    }

    export const r = foo();
}";
            EvaluateWithTypeCheckerDiagnostic(code, Cannot_find_name_0, "aaa");
        }

        [Fact]
        public void ErrorMessageOnMysmatchedTypeShouldNotHasObjectLiteral3InIt()
        {
            // Bug #579048
            string code =
@"function foo(n: number): number {
    return n + 1;
 }

export const r = foo(<any>{x: 42});";
            var result = EvaluateWithFirstError(code, "r");

            Assert.Equal(LogEventId.UnexpectedValueType, (LogEventId)result.ErrorCode);
            Assert.DoesNotContain("ObjectLiteral", result.FullMessage);
            Assert.Contains("'object literal'", result.FullMessage);
        }

        [Fact]
        public void ErrorMessageOnMysmatchedTypeShouldNotHasObjectLiteral3InItWhenSpecifiedInArray()
        {
            // Bug579048
            string code =
@"function foo(n: number): number {
    return n + 1;
 }

export const r = foo(<any>[{x: 42}]);";
            var result = EvaluateWithFirstError(code, "r");

            // For array we can't provide what the actual type would be, like number[], because there is not such information in runtime.
            // So the error would have Array.
            Assert.Equal(LogEventId.UnexpectedValueType, (LogEventId)result.ErrorCode);
            Assert.DoesNotContain("ObjectLiteral", result.FullMessage);
            Assert.Contains("'Array'", result.FullMessage);
        }

        [Fact]
        public void FaileWhenUndefinedFieldIsUsedInObjectLiteral()
        {
            // Bug568344
            string code =
@"function foo(n: {x: number}): number {
    return (<{x: number, y: number}>n).y + 1;
 }

export const r = foo({x: 42});";
            var result = EvaluateWithFirstError(code, "r");

            Assert.Equal(LogEventId.UnexpectedValueType, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void InterpreterShouldNotCrashOnEmptyFile()
        {
            // Bug600314
            // Parsing empty file shouldn't fail.
            Parse(string.Empty).NoErrors();
        }
    }
}
