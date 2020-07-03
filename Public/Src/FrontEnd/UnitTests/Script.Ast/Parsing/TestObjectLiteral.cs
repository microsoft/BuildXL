// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Parsing
{
    /// <summary>
    /// Test parsing object literals.
    /// </summary>
    [Trait("Category", "Parsing")]
    public class TestObjectLiteral : DsTest
    {
        public TestObjectLiteral(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestFailDuplicateIdentifiersInObjectLiteral()
        {
            var error = ParseWithDiagnosticId(@"
const x = {
    p: 0,
    p: 1
};", LogEventId.TypeScriptBindingError);

            Assert.Contains("Duplicate identifier", error.Message);
        }
    }
}
