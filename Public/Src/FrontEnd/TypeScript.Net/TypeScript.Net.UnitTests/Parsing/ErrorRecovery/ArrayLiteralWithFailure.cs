// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
