// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretTemplates : SemanticBasedTests
    {
        public InterpretTemplates(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TemplateInstanceShouldBeVisibleFromNamespaceWithoutExposedMembers()
        {
            var spec1 = @"
namespace X {
  export declare const template : {x: number} = {x: 42};
}

export const r = 42; // Fake value
";

            // Even though the namespace has only private value,
            // it still should be merged with the namespace X from another file.
            var spec2 = @"
namespace X {
   const v = template.x;
}
";

            var result = BuildWithPrelude()
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec1.dsc")
                .EvaluateExpressionWithNoErrors("r");

            Assert.Equal(42, result);
        }

        [Fact]
        public void DefaultTemplateIsTheEmptyObjectLiteral()
        {
            var code = @"
export declare const template : {defines: number} = {defines: 42};
const result = template.toString();
";

            var result = BuildWithPrelude()
                .AddSpec(code)
                .EvaluateExpressionWithNoErrors("result");

            AssertEqualDiscardingTrivia("{defines:42}", result);
        }

        [Fact]
        public void TemplateIsCapturedAndMerged()
        {
            var code = @"
namespace A {
    export declare const template : {defines: number} = {defines: 42};
}

namespace A.B {
    export declare const template : {enableAnalysis: boolean} = {enableAnalysis: true};
    export const result = template.toString();
}
";
            var result = BuildWithPrelude()
                .AddSpec(code)
                .EvaluateExpressionWithNoErrors("A.B.result");

            AssertEqualDiscardingTrivia("{defines:42,enableAnalysis:true}", result);
        }

        [Fact]
        public void TemplateIsCapturedAndMergedInChain()
        {
            var code = @"
namespace A {
    export declare const template : {defines: number[]} = {defines: [1]};
}

namespace A.B {
    export declare const template : {defines: number[]} = {defines: [2]};
}

namespace A.B.C {
    export declare const template : {defines: number[]} = {defines: [3]};
    export const result = template.toString();
}
";
            var result = BuildWithPrelude()
                .AddSpec(code)
                .EvaluateExpressionWithNoErrors("A.B.C.result");

            AssertEqualDiscardingTrivia("{defines:[1,2,3]}", result);
        }

        [Fact]
        public void TemplateIsMergedBasedOnNamespaceHierarchy()
        {
            var code = @"
namespace A {
    export declare const template : {defines: number[]} = {defines: [1]};
}

namespace A.B {
    export declare const template : {defines: number[]} = {defines: [2]};
    export const result = template.toString();
}

namespace A.Z {
    export declare const template : {defines: number[]} = {defines: [3]};
    export const result = template.toString();
}
";
            var result = BuildWithPrelude()
                .AddSpec(code)
                .EvaluateExpressionsWithNoErrors("A.B.result", "A.Z.result");

            AssertEqualDiscardingTrivia("{defines:[1,2]}", result["A.B.result"]);
            AssertEqualDiscardingTrivia("{defines:[1,3]}", result["A.Z.result"]);
        }

        [Fact]
        public void TopLevelCaptureOfDefaultTemplate()
        {
            var code = @"
export const x = f();

function f() {
    return Context.getTemplate().toString();
}";
            var result = BuildWithPrelude()
                .AddSpec(code)
                .EvaluateExpressionWithNoErrors("x");

            AssertEqualDiscardingTrivia("{}", result);
        }

        [Fact]
        public void TopLevelCaptureOfExplicitTemplate()
        {
            var code = @"
export declare const template: {defines: number} = {defines: 42};
export const x = f();

function f() {
    return Context.getTemplate().toString();
}";
            var result = BuildWithPrelude()
                .AddSpec(code)
                .EvaluateExpressionWithNoErrors("x");

            AssertEqualDiscardingTrivia("{defines:42}", result);
        }

        [Fact]
        public void NamespaceCaptureOfExplicitMergedTemplate()
        {
            var code = @"
namespace A {
    export declare const template: {defines: number[]} = {defines: [1]};
    export const r = f();
}

namespace A.B {
    export declare const template: {defines: number[]} = {defines: [2]};
    export const r = f();
}

namespace A.B.C {
    export const r = f();
}

namespace C {
    export declare const template: {defines: number[]} = {defines: [3]};
    export const r = f();
}

function f() {
    return Context.getTemplate().toString();
}";
            var result = BuildWithPrelude()
                .AddSpec(code)
                .EvaluateExpressionsWithNoErrors("A.r", "A.B.r", "A.B.C.r", "C.r");

            AssertEqualDiscardingTrivia("{defines:[1]}", result["A.r"]);
            AssertEqualDiscardingTrivia("{defines:[1,2]}", result["A.B.r"]);
            AssertEqualDiscardingTrivia("{defines:[1,2]}", result["A.B.C.r"]);
            AssertEqualDiscardingTrivia("{defines:[3]}", result["C.r"]);
        }

        [Fact]
        public void TemplateCaptureInModule()
        {
            var f = @"
export function f() {
    return Context.getTemplate().toString();
}";

            var code = @"
export declare const template: {defines: number[]} = {defines: [1]};

namespace A {
    export const r = f();
}";
            var result = BuildWithPrelude()
                .AddSpec("f.dsc", f)
                .AddSpec("build.dsc", code)
                .RootSpec("build.dsc")
                .EvaluateExpressionWithNoErrors("A.r");

            AssertEqualDiscardingTrivia("{defines:[1]}", result);
        }

        [Fact]
        public void TemplateCaptureViaImport()
        {
            var f = @"
@@public
export function f() {
    return Context.getTemplate().toString();
}";

            var code = @"
export declare const template: {defines: number[]} = {defines: [1]};

import * as F from ""F"";

namespace A {
    export const r = F.f();
}";
            var result = BuildWithPrelude()
                .AddSpec("f/package.config.dsc", V2Module("F"))
                .AddSpec("f/f.dsc", f)
                .AddSpec("build.dsc", code)
                .RootSpec("build.dsc")
                .EvaluateExpressionWithNoErrors("A.r");

            AssertEqualDiscardingTrivia("{defines:[1]}", result);
        }

        [Fact]
        public void TemplateCaptureInChainedCalls()
        {
            var code = @"
namespace A {
    export declare const template: {defines: number[]} = {defines: [1]};
    export const r = f();
}

function f() {
    return g();
}

function g() {
    return Context.getTemplate().toString();
}";
            var result = BuildWithPrelude()
                .AddSpec(code)
                .EvaluateExpressionWithNoErrors("A.r");

            AssertEqualDiscardingTrivia("{defines:[1]}", result);
        }

        [Fact]
        public void TemplateValidAfterImportStatement()
        {
            BuildLegacyConfigurationWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage"))
                .AddSpec(@"BPackage/package.config.dsc", V2Module("BPackage"))
                .AddSpec(@"APackage/package.dsc", @"
@@public
export interface TemplateType { f1: number; f2: string; }")
                .AddSpec(@"BPackage/package.dsc", @"
import * as A from ""APackage"";
export declare const template : A.TemplateType = { f1: 1, f2: ""hello"" };
export const x = Context.getTemplate().toString();")
                .RootSpec(@"BPackage/package.dsc")
                .EvaluateExpressionWithNoErrors("x");
        }

        [Fact]
        public void TemplateCycle()
        {
            var result = BuildWithPrelude()
                .AddSpec(@"APackage/package.config.dsc", V2Module("APackage").WithProjects("build.dsc", "bar.bxt"))
                .AddSpec(@"APackage/build.dsc", "export declare const template: {defines: File} = { defines: f`${x}/foo.exe` }; // export const y = foo(); export function foo() { return 2; }")
                .AddSpec(@"APackage/bar.bxt", String.Format("export const x = d`{0}`;", OperatingSystemHelper.IsUnixOS ? "/hey" : "b:/hey"))
                .RootSpec(@"APackage/bar.bxt")
                .EvaluateWithFirstError("x");

            Assert.Equal((int)LogEventId.Cycle, (int)result.ErrorCode);
        }

        [Fact]
        public void EndToEndExampleFromTheTemplateSpec()
        {
            var code = @"
interface SampleTemplate {
    sample: string;
}

namespace Product {
    export declare const template : SampleTemplate = {
        sample: ""FromProduct""
    };

    export const dll = Sdk.library();
    export const c = Sdk.compile(""case1"");
}

namespace Sdk {
    export declare const template : SampleTemplate = {
        sample: ""FromSdk""
    };

    export const fromSdk = compile(""case3"");

    export function library() {
        const a1 = template.sample; // ""FromSdk"" this is just a regular bound value
        Contract.assert(a1 === ""FromSdk"");

        const b1 = Context.getTemplate<SampleTemplate>().sample; // ""FromProduct"". This value was bound when the library function was called.
        Contract.assert(b1 === ""FromProduct"");

        compile(""case2"");
    }

    export function compile(caller: string) {
        const a1 = template.sample;         // ""FromSdk"" this is just a regular bound value
        Contract.assert(a1 === ""FromSdk"");

        const b1 = Context.getTemplate<SampleTemplate>().sample; // This differs from which case it is called:
                                                 // ""case1"" => ""FromProduct"". In this case compile is called directly from a top-level value in the Product
                                                 //                               namespace and binds the template from there to Context.library which has 'FromProduct'
                                                 // ""case2"" => ""FromProduct"". In this case compile is called from the library function which in this case already
                                                 //                               has a bound a Context.template bound, which has 'FromProduct'
                                                 // ""case3"" => ""FromSdk"".     In this case compile is called directly from top-level value in Sdk so the template
                                                 //                               in the Sdk namespace is bound, which has ""FromSdk""

        Contract.assert(caller !== ""case1"" || b1 === ""FromProduct"");
        Contract.assert(caller !== ""case2"" || b1 === ""FromProduct"");
        Contract.assert(caller !== ""case3"" || b1 === ""FromSdk"");
    }
}";
            BuildWithPrelude()
                .AddSpec(code)
                .EvaluateWithNoErrors();
        }
    }
}
