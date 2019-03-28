// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace Test.DScript.DScriptSpecific
{
    public sealed class TestWithQualifierGeneration
    {
        [Fact]
        public void WithQualifierIdentifierHasSourceFileAvailable()
        {
            var code = @"
export declare const qualifier : {};
namespace A {}

export const x = A.withQualifier({});";
            var parsedFile = ParsingHelper.ParseSourceFile(code);

            var identifier = NodeWalker.TraverseBreadthFirstAndSelf(parsedFile).OfType<IIdentifier>().First(n => n.Text == "withQualifier");
            
            // It is important for the injected modifiers to have an associated source file.
            Assert.NotNull(identifier.SourceFile);
        }

        [Fact]
        public void WithQualifierIsGeneratedForANamespace()
        {
            @"
export declare const qualifier : {};
namespace A {}

export const x = A.withQualifier({});"
            .TypeCheckAndAssertNoErrors();
        }

        [Fact]
        public void WithQualifierIsGeneratedForNestedNamespaces()
        {
            @"
export declare const qualifier : {};
namespace A {
    namespace B {
        export const x = 42;
    }
}

export const x = A.withQualifier({});
export const y = A.B.withQualifier({});"
            .TypeCheckAndAssertNoErrors();
        }

        [Fact]
        public void WithQualifierIsGeneratedForDottedNamespace()
        {
            @"
export declare const qualifier : {};
namespace A.B {
}

export const x = A.B.withQualifier({});"
            .TypeCheckAndAssertNoErrors();
        }

        /// <summary>
        /// This behavior is something we can consider changing if needed. It is just a slightly simpler to keep it as is.
        /// Consider that A and A.B in this case are always the same thing from a usage point of view.
        /// </summary>
        [Fact]
        public void WithQualifierIsNotGeneratedForAbsentNamespace()
        {
            @"
export declare const qualifier : {};
namespace A.B {
}

export const x = A.withQualifier({});"
            .TypeCheckAndAssertSingleError("Property 'withQualifier' does not exist on type 'typeof A'");
        }

        [Fact]
        public void WithQualifierReturnTypeContainsVisibleMember()
        {
            @"
export declare const qualifier : {};
namespace A {
    export const v = 42;
}

export const x = A.withQualifier({}).v;"
            .TypeCheckAndAssertNoErrors();
        }

        [Fact]
        public void WithQualifierReturnTypeDoesNotContainNonexistentMember()
        {
            @"
export declare const qualifier : {};
namespace A {
    export const v = 42;
}

export const x = A.withQualifier({}).doesNotExist;"
            .TypeCheckAndAssertSingleError("Property 'doesNotExist' does not exist on type 'typeof A'");
        }

        [Fact]
        public void WithQualifierParameterTypeIsTypeChecked()
        {
            @"
export declare const qualifier : {requiredField: string};
namespace A {
}

export const x = A.withQualifier({});"
                .TypeCheckAndAssertSingleError("Argument of type '{}' is not assignable to parameter of type '{ requiredField: string; }'");
        }

        [Fact]
        public void WithQualifierIsCompatibleWithNamespaceMerging()
        {
            @"
export declare const qualifier : {};
namespace A {
    export const v1 = 42;
}

namespace A {
    export const v2 = 42;
}
export const x = A.withQualifier({}).v1;
export const y = A.withQualifier({}).v2;"
            .TypeCheckAndAssertNoErrors();
        }
    }
}
