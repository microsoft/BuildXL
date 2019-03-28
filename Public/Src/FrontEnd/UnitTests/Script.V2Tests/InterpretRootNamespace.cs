// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Core.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretRootNamespace : SemanticBasedTests
    {
        public InterpretRootNamespace(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(@"
export const x = 42;
const y = $.x;
", "y")]
        [InlineData(@"
namespace A.B.C {
    export const x = 42;
}

namespace D.E.F {
    export const y = $.A.B.C.x;
}
", "D.E.F.y")]
        [InlineData(@"
namespace A {
    export const x = 42;
}

namespace B.A.C {
    export const y = $.A.x;
}
", "B.A.C.y")]
        [InlineData(@"
namespace A {
    export const x = 42;
    namespace B.C {
        export const y = $.A.x;
    }
}
", "A.B.C.y")]
        [InlineData(@"
namespace A {
    export const y = $.A.B.C.x;
    namespace B.C {
        export const x = 42;
    }
}
", "A.y")]
        public void RootNamespaceBindsToTheRoot(string code, string expressionToEvaluate)
        {
            var result = BuildWithPrelude()
                .AddSpec("spec.dsc", code)
                .RootSpec("spec.dsc")
                .EvaluateExpressionWithNoErrors(expressionToEvaluate);
            Assert.Equal(42, result);
        }

        [Theory]
        [InlineData(@"
namespace B.C {
    export const x = 42;
}")]
        [InlineData(@"
namespace B.C {
    @@public
    export const x = 42;
}")]
        public void RootNamespaceBindsToTheRootWithinModulePublicAndInternal(string spec2)
        {
            var spec1 = @"
namespace A.B.C {
    export const y = $.B.C.x;
}";
            var result = BuildWithPrelude()
                .AddSpec("A/package.config.dsc", V2Module("A"))
                .AddSpec("A/spec1.dsc", spec1)
                .AddSpec("A/spec2.dsc", spec2)
                .RootSpec("A/spec1.dsc")
                .EvaluateExpressionWithNoErrors("A.B.C.y");
            Assert.Equal(42, result);
        }

        [Fact]
        public void RootNamespaceCannotSeePrivateValues()
        {
            var spec1 = @"
namespace A.B.C {
    const y = $.B.C.x;
}
            
namespace B.C {
    const x = 42;
}";
            BuildWithPrelude()
                .AddSpec("spec1.dsc", spec1)
                .RootSpec("spec1.dsc")
                .EvaluateWithDiagnosticId(LogEventId.CheckerError, "A.B.C.y");
        }

        [Fact]
        public void RootNamespaceBindsToTheFileInLegacyModule()
        {
            var spec1 = @"
namespace A.B.C {
    export const y = $.B.x;
}

namespace B {
    export const x = 42;
}
";
            var result = BuildWithPrelude()
                .AddSpec("A/package.config.dsc", V1Module("A"))
                .AddSpec("A/spec1.dsc", spec1)
                .RootSpec("A/spec1.dsc")
                .EvaluateExpressionWithNoErrors("A.B.C.y");
            Assert.Equal(42, result);
        }

        [Fact]
        public void RootNamespaceDoesNotBindsToOtherFilesInLegacyModule()
        {
            var spec1 = @"
namespace A.B.C {
    const y = $.B.C.x;
}";
            var spec2 = @"
namespace B.C {
    export const x = 42;
}";
            BuildWithPrelude()
                .AddSpec("A/package.config.dsc", V1Module("A"))
                .AddSpec("A/spec1.dsc", spec1)
                .AddSpec("A/spec2.dsc", spec2)
                .RootSpec("A/spec1.dsc")
                .EvaluateWithDiagnosticId(LogEventId.CheckerError, "A.B.C.y");
        }

        [Theory]
        [InlineData("A.B.$.A.B.x")]
        [InlineData("$.$.A.B.x")]
        public void RootNamespaceCannotBeQualifiedOrChained(string expression)
        {
            var spec1 = $@"
const y = {expression};

namespace A.B {{
    export const x = 42;
}}";
            BuildWithPrelude()
                .AddSpec("spec1.dsc", spec1)
                .RootSpec("spec1.dsc")
                .EvaluateWithDiagnosticId(LogEventId.CheckerError, "y");
        }

        [Fact]
        public void RootNamespaceIsARegularValue()
        {
            var code = @"
export const x = 42;
const root = $;
export const y = root.x;
";

            var result = BuildWithPrelude()
                   .AddSpec("spec.dsc", code)
                   .RootSpec("spec.dsc")
                   .EvaluateExpressionWithNoErrors("y");
            Assert.Equal(42, result);
        }

        [Fact]
        public void RootNamespaceIsARegularValueAndCanBeExported()
        {
            var spec1 = @"
export const x = 42;
const root = $;

export {root};
";
            var spec2 = @"
export const z = root.x;
";

            var result = BuildWithPrelude()
                   .AddSpec("spec1.dsc", spec1)
                   .AddSpec("spec2.dsc", spec2)
                   .RootSpec("spec2.dsc")
                   .EvaluateExpressionWithNoErrors("z");
            Assert.Equal(42, result);
        }
    }
}
