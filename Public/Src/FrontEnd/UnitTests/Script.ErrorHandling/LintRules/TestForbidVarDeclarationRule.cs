// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidVarDeclarationRule : DsTest
    {
        public TestForbidVarDeclarationRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestVarDeclarationIsNotAllowed()
        {
            string code = @"var x: string = ""this is not allowed"";";

            ParseWithDiagnosticId(code, LogEventId.OnlyConstBindingOnNamespaceLevel);
        }

        [Fact]
        public void TestMultipleVarDeclarationIsNotAllowed()
        {
            string code = @"var x = 1, y = 2, z = 3;";

            ParseWithDiagnosticId(code, LogEventId.OnlyConstBindingOnNamespaceLevel);
        }
    }
}
