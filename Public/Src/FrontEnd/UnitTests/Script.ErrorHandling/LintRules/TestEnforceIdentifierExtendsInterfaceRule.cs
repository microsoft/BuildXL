// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceIdentifierExtendsInterfaceRule : DsTest
    {
        public TestEnforceIdentifierExtendsInterfaceRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestImplementsIsNotAllowed()
        {
            string code =
                @"interface T implements T1 { }";

            ParseWithDiagnosticId(code, LogEventId.OnlyExtendsClauseIsAllowedInHeritageClause);
        }

        [Fact]
        public void TestOnlyIdentifiersExtendsInterfaces()
        {
            string code =
                @"interface T extends {x: number} { }";

            ParseWithDiagnosticId(code, LogEventId.InterfacesOnlyExtendedByIdentifiers);
        }
    }
}
