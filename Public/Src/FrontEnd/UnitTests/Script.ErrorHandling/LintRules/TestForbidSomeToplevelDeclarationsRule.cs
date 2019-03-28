// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidSomeToplevelDeclarationsRule : DsTest
    {
        public TestForbidSomeToplevelDeclarationsRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [ClassData(typeof(RestrictedTopLevelStatements))]
        public void TestNotAllowedTopLevelStatements(string statement, LogEventId expectedErrorCode)
        {
            ParseWithDiagnosticId(statement, expectedErrorCode);
        }

        [Theory]
        [ClassData(typeof(RestrictedTopLevelStatements))]
        public void TestNotAllowedNamespaceLevelStatements(string statement, LogEventId expectedErrorCode)
        {
            string code = $@"
namespace M {{
   {statement}
}}";
            ParseWithDiagnosticId(code, expectedErrorCode);
        }

        [Fact]
        public void EmptyStatementsAreAllowed()
        {
            string code = $@"
export function f() {{}};
;;
namespace M {{
    export function g() {{}};
    ;;
}}
export const x = 42;
";
            var result = EvaluateExpressionWithNoErrors(code, "x");
        }

        [Theory]
        [InlineData(Names.ConfigurationFunctionCall)]
        [InlineData(Names.LegacyModuleConfigurationFunctionCall)]
        [InlineData(Names.ModuleConfigurationFunctionCall)]
        [InlineData("randomFunctionCall")]
        public void TestNotAllowedNamespaceConfigurationCall(string functionCall)
        {
            string code = $@"
namespace M {{
    {functionCall}({{}});
}}";
            ParseWithDiagnosticId(code, LogEventId.FunctionApplicationsWithoutConstBindingAreNotAllowedTopLevel);
        }
    }

    internal class RestrictedTopLevelStatements : IEnumerable<object[]>
    {
        public IEnumerator<object[]> GetEnumerator()
        {
            yield return new object[] {"for (let i = 0 ; i < 10; i = i + 1) {;}",   LogEventId.OnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel};
            yield return new object[] {"for (let i of [1,2]) {;}",                  LogEventId.OnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel};
            yield return new object[] {"while (false) {;}",                         LogEventId.OnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel};
            yield return new object[] {"if (true) {;}",                             LogEventId.OnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel};
            yield return new object[] {"switch (0) {default:}",                     LogEventId.OnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel};
            yield return new object[] {"try {;} catch (error) {;} finally {;}",     LogEventId.OnlyTypeAndFunctionDeclarationsAndConstBindingsAreAllowedTopLevel};
            yield return new object[] {"5 + 3;",                                    LogEventId.FunctionApplicationsWithoutConstBindingAreNotAllowedTopLevel};
            yield return new object[] {"randomFunctionCall();",                     LogEventId.FunctionApplicationsWithoutConstBindingAreNotAllowedTopLevel};
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
