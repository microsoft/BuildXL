// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Instrumentation.Common;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceWellShapedInlineImportRule : DScriptV2Test
    {
        public TestEnforceWellShapedInlineImportRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestImportFromWithInvalidArgumentNumber()
        {
            string code = @"
namespace M {
    export const result = importFrom(42);
}";
            var result = TestSimpleCode(code);
            ValidateErrorText(TypeScript.Net.Diagnostics.Errors.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1.Message, result.Message, "number", "string");
        }

        [Fact]
        public void TestImportFrom()
        {
            string code = @"
namespace M {
    export const result = importFrom(""APackage"").x;
}";

            var result = BuildWithPrelude()
                .AddFile("APackage/package.config.dsc", CreatePackageConfig("APackage", false, "package.dsc"))
                .AddFile("APackage/package.dsc", "export const x = 42;")
                .AddFile("BPackage/package.config.dsc", CreatePackageConfig("BPackage", false, "package.dsc"))
                .AddSpec("BPackage/package.dsc", code)
                .RootSpec("BPackage/package.dsc")
                .EvaluateExpressionWithNoErrors("M.result");

            Assert.Equal(42, result);
        }

        [Fact]
        public void TestImportFromWithInvalidArgument()
        {
            string code = @"
namespace M {
    export const result = importFrom(f(42));
}";
            var result = TestSimpleCode(code);
            ValidateErrorText(TypeScript.Net.Diagnostics.Errors.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1.Message, result.Message, "number", "string");
        }

        [Fact]
        public void TestImportFileWithInvalidArgument()
        {
            string code = @"
namespace M {
    export const result = importFile(f(42));
}";
            var result = TestSimpleCode(code);
            ValidateErrorText(TypeScript.Net.Diagnostics.Errors.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1.Message, result.Message, "number", "string");
        }

        private Diagnostic TestSimpleCode(string code)
        {
            return BuildWithPrelude()
                .AddFile("package.config.dsc", CreatePackageConfig("MyPackage", false, "package.dsc"))
                .AddFile("package.dsc", code)
                .RootSpec("package.dsc")
                .EvaluateWithFirstError("M.result");
        }
    }
}
