// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceVariableInitializationRule : DsTest
    {
        public TestEnforceVariableInitializationRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestVariableInNamespaceMustBeIntialized()
        {
            string code = @"
namespace M {
    const x;   
}

export const result = M.x;
";
            ParseWithDiagnosticId(code, LogEventId.VariableMustBeInitialized);
        }

        [Fact]
        public void TestVariableInFunctionMustBeIntialized()
        {
            string code = @"
namespace M {
    export function foo() {
       const x;
       return 1;
    }
}

export const result = foo();
";
            ParseWithDiagnosticId(code, LogEventId.VariableMustBeInitialized);
        }

        [Fact]
        public void TestAmbientVariableDeclarationMayNotBeInitialized()
        {
            string code = @"declare const x: string;";
            var result = Parse(code);

            result.ExpectNoError();
        }
    }
}
