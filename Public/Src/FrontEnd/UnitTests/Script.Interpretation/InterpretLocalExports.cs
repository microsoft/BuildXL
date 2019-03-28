// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

using static TypeScript.Net.Diagnostics.Errors;

namespace Test.DScript.Ast.Interpretation
{
    /// <summary>
    /// Tests interactions with 'export {names};'
    /// </summary>
    public sealed class InterpretLocalExports : DsTest
    {
        public InterpretLocalExports(ITestOutputHelper output)
            : base(output)
        {
        }
        
        [Fact]
        public void TestLocalExportDoesNotIntroduceLocalNames()
        {
            string project1 = @"
const x = 42;

export {x as w};

const y = w;
";

            Build()
                .AddSpec("project1.dsc", project1)
                .RootSpec("project1.dsc")
                .EvaluateWithCheckerDiagnostic(Cannot_find_name_0, "w");
        }

        [Fact]
        public void TestLocalExportCannotExportAlreadyExportedName()
        {
            string project1 = @"
export const x = 42;
export {x};
";

            Build()
                .AddSpec("project1.dsc", project1)
                .RootSpec("project1.dsc")
                .EvaluateWithCheckerDiagnostic(Cannot_redeclare_exported_variable_0, "x");
        }

        [Fact]
        public void TestLocalExportAsCannotExportAlreadyExportedName()
        {
            string project1 = @"
const x = 42;
export const y = 43;
export {x as y};
";

            Build()
                .AddSpec("project1.dsc", project1)
                .RootSpec("project1.dsc")
                .EvaluateWithCheckerDiagnostic(Cannot_redeclare_exported_variable_0, "y");
        }
        
        [Fact]
        public void TestLocalExportFailsOnInvalidReference()
        {
            string project1 = @"
export {x};
";

            Build()
                .AddSpec("project1.dsc", project1)
                .RootSpec("project1.dsc")
                .EvaluateWithCheckerDiagnostic(Cannot_find_name_0, "x");
        }

        [Fact]
        public void TestLocalExportFailsOnInvalidNamespaceReference()
        {
            string project1 = @"
export {N};
";

            Build()
                .AddSpec("project1.dsc", project1)
                .RootSpec("project1.dsc")
                .EvaluateWithCheckerDiagnostic(Cannot_find_name_0, "N");
        }
    }
}
