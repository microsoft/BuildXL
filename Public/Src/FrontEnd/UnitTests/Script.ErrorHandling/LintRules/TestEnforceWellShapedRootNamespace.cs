// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceWellShapedRootNamespace : SemanticBasedTests
    {
        public TestEnforceWellShapedRootNamespace(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("namespace $ {}")]
        [InlineData("namespace A.$ {}")]
        [InlineData("export const enum $ { one }")]
        [InlineData("function $() {}")]
        [InlineData("interface $ {}")]
        [InlineData("type $ = number;")]
        [InlineData("import * as $ from 'MyModule';")]
        [InlineData("import {x as $} from 'MyModule';")]
        [InlineData("const x = 42; export {x as $};")]
        public void TestRootNamespaceNameIsAKeyworkd(string code)
        {
            var module = @"
@@public
export const x = 42;";

            BuildWithPrelude()
                .AddSpec("MyModule/package.config.dsc", V2Module("MyModule"))
                .AddSpec("MyModule/spec.dsc", module)
                .AddSpec(code)
                .EvaluateWithDiagnosticId(LogEventId.RootNamespaceIsAKeyword);
        }
    }
}
