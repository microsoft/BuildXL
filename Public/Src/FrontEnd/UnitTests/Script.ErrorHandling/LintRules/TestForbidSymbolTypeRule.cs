// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidSymbolTypeRule : DsTest
    {
        public TestForbidSymbolTypeRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestSymbolTypeIsNotSupported()
        {
            string code = @"
namespace M {
    const x: symbol = undefined;   
}

export const result = M.x;
";
            ParseWithDiagnosticId(code, LogEventId.NotSupportedSymbolKeyword);
        }
    }
}
