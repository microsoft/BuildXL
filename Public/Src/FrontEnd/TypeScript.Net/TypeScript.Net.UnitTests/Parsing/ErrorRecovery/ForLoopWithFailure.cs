// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing.ErrorRecovery
{
    public class ForLoopWithFailure
    {
        [Fact]
        public void ForLoopWithCSharpStyleOfDeclarationShouldNotCrash()
        {
            string code = @"for (int i = 0 ; i < 10; i = i + 1) {;}";
            var diagnostics = ParsingHelper.ParseAndGetDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }
    }
}
