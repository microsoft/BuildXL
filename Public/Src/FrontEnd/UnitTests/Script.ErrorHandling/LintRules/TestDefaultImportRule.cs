// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestDefaultImportRule : DsTest
    {
        public TestDefaultImportRule(ITestOutputHelper output)
            : base(output)
        { }

        [Fact]
        public void WarnOnDefaultImports()
        {
            string code = @"
import x from 'Sdk.Prelude';";

            var result = Build()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.DefaultImportsNotAllowed);
        }
    }
}
