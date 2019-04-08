// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Tracing;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;
using System.Diagnostics;

namespace Test.DScript.Ast.DScriptV2
{
    public class TestQualifierCoercion : SemanticBasedTests
    {
        public TestQualifierCoercion(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void CoerceQualifierTypeWithNestedNamespace()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const r = qualifier.v;

  namespace Y {
    export declare const qualifier: {v: '42'};
    export const r = qualifier.v;
  }
}

export const x = X.withQualifier({v: '42'});
export const r1 = x.Y.r;
export const r2 = x.r;
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal("42", result["r1"]);
            Assert.Equal("42", result["r2"]);
        }

        [Fact]
        public void CoerceQualifierTypeWithNestedNamespaceWithLambda()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};

  namespace Y {
    export declare const qualifier: {v: '42'};
    export const getR = () => {return qualifier.v;};
  }
}

export const x = X.withQualifier({v: '42'});
export const r = x.Y.getR();
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal("42", result);
        }

        [Fact]
        public void EvaluateNestedExpressionWithGivenQualifier()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const r = qualifier.v;
}

export const r = X.r; // Fail, top most r has an empty qualifier
";
            var result = BuildLegacyConfigurationWithPrelude()
                .AddSpec("Module1/package.config.dsc", V2Module("Module1").WithProjects("spec1.dsc", "spec2.dsc"))
                .AddSpec("Module1/spec2.dsc", code)
                .AddSpec("Module1/spec1.dsc", "export const foo = X.r;")
                .Qualifier("{v: '42'}")
                .RootSpec("Module1/spec1.dsc")
                .EvaluateWithFirstError("foo");
            Assert.Equal(LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, (LogEventId)result.ErrorCode);
            Assert.EndsWith("spec1.dsc", result.Location?.File);
        }

        [Fact]
        public void QualifierCoercionWithExplicitWithQualifierCall()
        {
            string code =
@"namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const value = qualifier.v;

  namespace Y {
     // qualifier is inherited from the enclosing namespace X
     export const r = value;
  }
}

export const r = X.withQualifier({v: '42'}).Y.r; // 42

";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal("42", result);
        }

        [Fact]
        public void CoerceQualifierTypeWithNestedNamespaceAndNarrowing()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};

  namespace Y {
    export declare const qualifier: {v: '42'};
    export const r = qualifier.v;
  }
}
// object literal can not be extracted into a variable, because the typechecker will complain.
export const x = X.withQualifier(<any>{v: '42', z: '42'});
export const r = x.Y.r;
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionWithNoErrors("r");
            Assert.Equal("42", result);
        }

        [Fact]
        public void QualifierTypeCoercionFailesWithNestedNamespace()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const r = qualifier.v;

  namespace Y {
    export declare const qualifier: {v: '42'};
    export const r = qualifier.v;
  }
}

const x = X.withQualifier({v: '36'}); // OK
export const r2 = x.r; // OK
export const r3 = x.Y.r; // failure, because Y doesn't have '36' as a valid value!
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateWithFirstError();
            Assert.Equal(LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void QualifierTypeCoercionFailesWithNestedNamespaceHadWiderQualifierType()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const r = qualifier.v;

  namespace Y {
    export declare const qualifier: {v: '42', z: 'x'};
    export const r = qualifier.v;
  }
}

const x = X.withQualifier({v: '42'}); // OK
const y = x.Y;
export const r1 = y.r; // failure, because Y has excessive property!
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateWithFirstError("r1");
            Assert.Equal(LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void QualifierTypeCoercionFailesWithNestedNamespaceHadWiderQualifierTypeFromFunctionInvocation()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const r = qualifier.v;

  namespace Y {
    export declare const qualifier: {v: '42', z: 'x'};
    export const r = qualifier.v;
  }
}

const x = X.withQualifier({v: '42'}); // OK
function getY() {return x.Y; }
export const r1 = getY().r; // failure, because Y has excessive property!
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateWithFirstError("r1");
            Assert.Equal(LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void QualifierTypeCoercionFailesWhenFunctionIsInTheOuterScope()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '36'};
  export function getR() {return qualifier.v;}

  namespace Y {
    export declare const qualifier: {v: '42'};
    export const r = getR();
  }
}

// Fail on line r = getR() because the execution will cross the namespace boundary
// with incompatible qualifier types.
export const r1 = X.Y.withQualifier({v: '42'}).r; // failure,
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateWithFirstError("r1");
            Assert.Equal(LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void CoercionHappensForExplicitWithQualifierCall()
        {
            string code =
@"namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const r = qualifier.v;

  namespace Y {
    export declare const qualifier: {v: '42'|'36'};
    export const r = qualifier.v;
  }
}

// Need to cast to any, because 'qualifier' doesn't match to an argument
export const y = X.withQualifier(<any>qualifier).Y; // failure here normally cought by the type checker

// This means that X.Y != X.withQualifier(<any>qualifier).Y
// X.Y doesn't trigger coercion, but X.withQualifier(qualifier).Y - DOES!
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateWithFirstError();
            Assert.Equal(LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, (LogEventId)result.ErrorCode);
        }

        // This is a good candidate for docs
        [Fact]
        public void CoercionOnlyHappensOnValues()
        {
            string code =
@"export declare const qualifier: {};
namespace X {
  export declare const qualifier: {prop: 'x'|'y'|'z'};
  export const r = qualifier.prop;

  namespace Y {
    export declare const qualifier: {};
    export const r = 42;
  }
}

export const r1 = X.Y.r; // 42, because no coercion happens once the evaluation crosses X namespace
export const r2 = X.withQualifier({prop: 'x'}).r; // 'x'
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateExpressionsWithNoErrors("r1", "r2");
            Assert.Equal(42, result["r1"]);
            Assert.Equal("x", result["r2"]);
        }

        // This is a good candidate for docs
        [Fact]
        public void CoercionOnlyHappensOnValuesOnNestedNamespacesWithNamespaceAsAValue()
        {
            string code =
@"
namespace X {
  export declare const qualifier: {v: '42'|'36'};
  export const value = qualifier.v;

  namespace Y {
    export declare const qualifier: {};
    // This will not trigger a coercion, because the right hand side is a namespace.
    export const r = X;
  }
}

export const r = X.withQualifier({v: '42'}).Y.r.value; // ok, sorry, guys! No coercion!

";
            var result = BuildLegacyConfigurationWithPrelude()
                .Spec(code)
                .EvaluateExpressionWithNoErrors("r");
            Assert.Equal("42", result);
        }

        [Fact]
        public void QualifierTypeCoercionFailesWithNestedNamespaceAndExplicitWithQualifierCallWhenWiderQualifierTypeIsUsed()
        {
            string code =
                @"
namespace X {
  export declare const qualifier: {v: '42'|'36'};

  namespace Y {
    export declare const qualifier: {v: '42'};
    export const r = qualifier.v;
  }
}

// With no cast to 'any' this code will fail by the checker.
const q = {v: '36', extra: '-1'};
export const r = X.Y.withQualifier(<any>q).r; // failure, because Y doesn't have '36' as a valid value!
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateWithFirstError();
            Assert.Equal(LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void CrossingFromNestedNamespaceToTheOuterOneFailsIfOuterNamespaceHasWeakerQualifierSpace()
        {
            string code =
@"
namespace X {
  export declare const qualifier: {v: '42'};
  export const fromX = qualifier.v;

  namespace Y {
    export declare const qualifier: {v: '42'|'36'};
    // Crossing from the nested namespace to the outer one.
    export const r = fromX; // '$.X.fromX'
  }
}

export const r = X.Y.withQualifier({v: '36'}).r; // failure, because Y doesn't have '36' as a valid value!
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).EvaluateWithFirstError();
            Assert.Equal(LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void CrossingFromNestedNamespaceToTheOuterOneFailsIfOuterNamespaceHasWeakerQualifierSpaceWithGlobal()
        {
            string code =
                @"
// This is not qualified variable, but the qualifier for a file (i.e. for a top-level '$'-like namespace is empty
export declare const qualifier: {v: '42'};
export const globalEntry = qualifier.v;

namespace X {

  namespace Y {
    export declare const qualifier: {v: '42'|'36'};
    // Crossing from the nested namespace to the outer one.
    export const r = globalEntry;
  }
}

// Implicit type coercion is happening when the execution crosses from Y.r to globalEntry.
// Current qualifier for Y.r will be '36' and coercion fails because global namespace's qualifier doesn't have '36'.
export const r = X.Y.withQualifier({v: '36'}).r;
";
            var result = BuildLegacyConfigurationWithPrelude().Spec(code).Qualifier("{v: '42'}").EvaluateWithFirstError("r");
            Assert.Equal(LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void TestImportDoesNotTriggerCoercion()
        {
            string spec1 = @"
export declare const qualifier: {v: '42'};

namespace X {
    export const x = 42;
}";

            string spec2 = @"
export declare const qualifier: {};
// This import shouldn't trigger coercion (that should fail if it did)
export const M1 = importFrom(""Module1"");
";
            BuildLegacyConfigurationWithPrelude()
                .AddSpec("Module1/package.dsc", spec1)
                .AddSpec("Module1/package.config.dsc", V2Module("Module1"))
                .AddSpec("appSpec.dsc", spec2)
                .RootSpec("appSpec.dsc").EvaluateWithNoErrors();
        }

        [Fact]
        public void TestImportFromWithQualifierWhichCannotBeCoerced()
        {
            string spec1 = @"
export declare const qualifier: {v: '42'};

namespace X {
    @@public
    export const x = 42;
}";

            string spec2 = @"
export declare const qualifier: {};
export const M1 = importFrom(""Module1"").X.x;
";
            var errors = BuildLegacyConfigurationWithPrelude()
                .AddSpec("Module1/package.dsc", spec1)
                .AddSpec("Module1/package.config.dsc", V2Module("Module1"))
                .AddSpec("appSpec.dsc", spec2)
                .RootSpec("appSpec.dsc")
                .EvaluateWithDiagnostics();

            var error = errors.FirstOrDefault(d => d.ErrorCode == (int)LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance);
            Assert.NotNull(error);
            Assert.Equal((int)LogEventId.QualifierCannotBeCoarcedToQualifierSpaceWithProvenance, error.ErrorCode);
            Assert.Contains("appSpec.dsc(3,19)", error.Message);
            Assert.EndsWith("Module1" + Path.DirectorySeparatorChar + "package.dsc(6,18)'.", error.Message);
        }
    }
}
