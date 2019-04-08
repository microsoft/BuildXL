// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.DScriptV2
{
    public sealed class TestAccessesOnAny: DScriptV2Test
    {
        public TestAccessesOnAny(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void AccessOnExplicitAnyIsDenied()
        {
            BuildWithPrelude()
               .AddSpec("package.dsc", @"
const x : any = 42;
const y = x.aField;
")
               .RootSpec("package.dsc")
               .EvaluateWithDiagnosticId(LogEventId.PropertyAccessOnValueWithTypeAny, "y");
        }

        [Fact]
        public void AccessOnImplicitAnyIsDenied()
        {
            BuildWithPrelude()
                .AddSpec("package.dsc", @"
function f(): any {
    return 1;
}

const x = f();
const y = x.aField;
")
                .RootSpec("package.dsc")
                .EvaluateWithDiagnosticId(LogEventId.PropertyAccessOnValueWithTypeAny, "y");
        }

        [Fact]
        public void ToStringOnAnyIsAllowed()
        {
            BuildWithPrelude()
                .AddSpec("package.dsc", @"
const x : any = 42;
const y = x.toString();
")
                .RootSpec("package.dsc")
                .EvaluateWithNoErrors("y");
        }

        [Fact]
        public void AnyWithNoAccessesIsAllowed()
        {
            BuildWithPrelude()
                .AddSpec("package.dsc", @"
interface I {a : any}
const x : I = {a : 42};
const y = x.a;
")
                .RootSpec("package.dsc")
                .EvaluateWithNoErrors("y");
        }

        [Fact]
        public void ChainedAnyIsDenied()
        {
            BuildWithPrelude()
                .AddSpec("package.dsc", @"
interface I {a : any}
const x : I = {a : 42};
const y = x.a.anyField;
")
                .RootSpec("package.dsc")
                .EvaluateWithDiagnosticId(LogEventId.PropertyAccessOnValueWithTypeAny, "y");
        }
    }
}
