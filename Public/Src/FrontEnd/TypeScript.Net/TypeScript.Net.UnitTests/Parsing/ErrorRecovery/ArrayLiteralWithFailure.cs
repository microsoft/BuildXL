// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing.ErrorRecovery
{
    public class ArrayLiteralWithFailure
    {
        [Fact]
        public void NotClosedArrayLiteral()
        {
            string code =
@"{
    let x: string[] = [;
}";
            var diagnostics = ParsingHelper.ParseAndGetDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }

        [Fact]
        public void IdentifierOnNumericLiteral()
        {
            string code =
@"{
    let x: 1.something;
}";
            var diagnostics = ParsingHelper.ParseAndGetDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }
    }
}
