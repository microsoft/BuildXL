// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.DScriptV2
{
    public class TestSemanticBasedQualifiers : SemanticBasedTests
    {
        public TestSemanticBasedQualifiers(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestSimpleQualifiedNamespace()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const r = qualifier.v;
  export const r2 = {x: qualifier.v};
}

export const r1 = X.withQualifier({v: '42'}).r;
export const r2 = X.withQualifier({v: '36'}).r2.x;
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal("42", result["r1"]);
            Assert.Equal("36", result["r2"]);
        }

        [Fact]
        public void TestQualifierAliasing()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const r = qualifier.v;
  export const r2 = {x: qualifier.v};
}

const x1 = X.withQualifier({v: '42'});
const x2 = X.withQualifier({v: '36'});

export const r1 = x1.r;
export const r2 = x2.r2.x;
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal("42", result["r1"]);
            Assert.Equal("36", result["r2"]);
        }

        [Fact]
        public void TestQualifierPropertyTypeAliasing()
        {
            string code =
                @"
namespace X {
  type aliasedType = '42'|'36';
  type aliasOfAlias = aliasedType;
  export declare const qualifier: {v: aliasOfAlias | '54'};
  export const r = qualifier.v;
  export const r2 = {x: qualifier.v};
}

const x1 = X.withQualifier({v: '42'});
const x2 = X.withQualifier({v: '54'});

export const r1 = x1.r;
export const r2 = x2.r2.x;
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal("42", result["r1"]);
            Assert.Equal("54", result["r2"]);
        }

        [Fact]
        public void TestQualifierCoercionWhenCrossingNamespaceBoundariesWithExplicitWithQualifiersInside()
        {
            string code =
@"export declare const qualifier: {v: '42'|'36'};

namespace X {
  export const tmp = qualifier.v;
  export const r1 = Y.r1;
  export const r2 = Y.r2;
}
namespace Y {
  export const r1 = X.withQualifier({v: '42'}).tmp;
  export const r2 = X.withQualifier({v: '36'}).tmp;
  
}

export const r1 = X.r1;
export const r2 = X.r2;
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).Qualifier("{v: '36'}").EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal("42", result["r1"]);
            Assert.Equal("36", result["r2"]);
        }

        [Fact]
        public void TestQualifiedFunctionInvocationAliasing()
        {
            string code =
@"
namespace X {
  export declare const qualifier: {v: '42'|'36'};
  const r = qualifier.v;
  export function getR() {return r;}
  export function getR2() {return {x: r};}
}
const x1 = X.withQualifier({v: '42'});
export const r1 = x1.getR();
export const r2 = X.withQualifier({v: '36'}).getR2().x;
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal("42", result["r1"]);
            Assert.Equal("36", result["r2"]);
        }

        [Fact]
        public void TestNestedDottedQualifiedName()
        {
            string code =
@"namespace X {
  export declare const qualifier: {prop: 'x'|'y'|'z'};

  export const r = qualifier.prop;
  export const y = Y.withQualifier({prop: 'y'});

  namespace Y {
    export const z = Z.withQualifier({prop: 'z'});
    namespace Z {
      export const r = qualifier.prop;
    }
  }
}

const x = X.withQualifier({prop: 'x'});

export const r1 = x.r; // 'x'
export const r2 = x.y.z.r; // 'z'
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal("x", result["r1"]);
            Assert.Equal("z", result["r2"]);
        }

        [Fact]
        public void InterpretQualifiedImportStarFrom()
        {
            string spec1 =
@"namespace X {
    export declare const qualifier: {x: '42'|'36'};
    @@public
    export const v = qualifier.x;
}";

            string spec2 =
@"import * as s from 'MyPackage';
const x = s.X.withQualifier({x: '42'});
export const r = x.v;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("A/package.config.dsc", V2Module("MyPackage"))
                .AddSpec("A/package.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec2.dsc")
                .EvaluateExpressionWithNoErrors("r");
            Assert.Equal("42", result);
        }

        [Fact]
        public void InterpretQualifiedImportFrom()
        {
            string spec1 =
@"export namespace X {
    export declare const qualifier: {x: '42'|'36'};
    @@public
    export const v = qualifier.x;
}";

            string spec2 =
@"const x = importFrom('MyPackage').X.withQualifier({x: '42'});
export const r = x.v;";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("A/package.config.dsc", V2Module("MyPackage"))
                .AddSpec("A/spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .RootSpec("spec2.dsc")
                .EvaluateExpressionWithNoErrors("r");
            Assert.Equal("42", result);
        }

        [Fact]
        public void InterpretQualifiedNamespacesInOneFile()
        {
            string code =
@"namespace X {
  export declare const qualifier: {x: '1'|'3', y: '2'|'4'};
  export const x = qualifier.x;
}

namespace X {
  export const y = qualifier.y;
}

const x1 = X.withQualifier({x: '1', y: '2'});
const x2 = X.withQualifier({x: '3', y: '4'});

export const r1 = x1.x; //1
export const r2 = x1.y; //2
export const r3 = x2.x; //3
export const r4 = x2.y; //4";

            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2", "r3", "r4");
            Assert.Equal("1", result["r1"]);
            Assert.Equal("2", result["r2"]);
            Assert.Equal("3", result["r3"]);
            Assert.Equal("4", result["r4"]);
        }

        [Fact]
        public void InterpretQualifiedNamespacesInDifferentFiles()
        {
            string spec1 =
@"namespace X {
  export declare const qualifier: {x: '1'|'3', y: '2'|'4'};
  export const x = qualifier.x;
  export const fromAnotherNsDeclaration = y;
}";
            string spec2 =
@"namespace X {
  export const y = qualifier.y;
}";
            string spec3 =
@"const x1 = X.withQualifier({x: '1', y: '2'});
const x2 = X.withQualifier({x: '3', y: '4'});

export const r1 = x1.x; //1
export const r2 = x1.fromAnotherNsDeclaration; //2
export const r3 = x1.y; //2

export const r4 = x2.x; //3
export const r5 = x2.fromAnotherNsDeclaration; //3
export const r6 = x2.y; //4";

            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("spec1.dsc", spec1)
                .AddSpec("spec2.dsc", spec2)
                .AddSpec("spec3.dsc", spec3)
                .RootSpec("spec3.dsc")
                .EvaluateExpressionsWithNoErrors("r1", "r2", "r3", "r4", "r5", "r6");

            Assert.Equal("1", result["r1"]);
            Assert.Equal("2", result["r2"]);
            Assert.Equal("2", result["r3"]);
            Assert.Equal("3", result["r4"]);
            Assert.Equal("4", result["r5"]);
            Assert.Equal("4", result["r6"]);
        }

        [Fact]
        public void TestConfigQualifierIsNotInheritedWhenModuleHasImplicitSemantics()
        {
            string spec = @"
namespace M 
{
    export const p = qualifier.platform;
}";

            BuildLegacyConfigurationWithPrelude()
                .AddSpec(spec)
                .Evaluate()
                .ExpectErrorMessageSubstrings(new[] { "Property 'platform' does not exist on type" });
        }
    }
}
