// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidExportsInNamespace : DsTest
    {
        public TestForbidExportsInNamespace(ITestOutputHelper output)
            : base(output)
        { }

        [Fact]
        public void FailWhenLocalExportIsInANamespace()
        {
            string foo = @"
namespace M {
    export {name};
}";

            var result = Build()
                .AddSpec("foo.dsc", foo)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.ExportsAreNotAllowedInsideNamespaces);
        }

        [Fact]
        public void FailWhenExportFromIsInANamespace()
        {
            string foo = @"
namespace M {
    export {name} from 'blah';
}";

            var result = Build()
                .AddSpec("foo.dsc", foo)
                .RootSpec("foo.dsc")
                .EvaluateWithDiagnostics();

            result.ExpectErrorCode(LogEventId.ExportsAreNotAllowedInsideNamespaces);
        }
    }
}
