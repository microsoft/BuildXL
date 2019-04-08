// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.DScript;
using TypeScript.Net.UnitTests.TypeChecking;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.TypeChecking
{
    public sealed class PublicDecoratorTests
    {
        private static readonly ParsingOptions ParsingOptions = new ParsingOptions(
            namespacesAreAutomaticallyExported: true,
            generateWithQualifierFunctionForEveryNamespace: false,
            preserveTrivia: false,
            allowBackslashesInPathInterpolation: true,
            useSpecPublicFacadeAndAstWhenAvailable: false,
            escapeIdentifiers: true);

        private readonly ITestOutputHelper m_output;

        public PublicDecoratorTests(ITestOutputHelper output)
        {
            m_output = output;
        }

        [Theory]
        [InlineData(@"
@@public
export interface I{}")]
        [InlineData(@"
namespace X {
    @@public
    export interface I{}
}")]
        [InlineData(@"
namespace X.Y {
    @@public
    export interface I{}
}")]
        [InlineData(@"
@@public
export const x = 42;")]
        [InlineData(@"
@@public
export function g(){}")]
        [InlineData(@"
@@public
export const enum MyEnum { case1 }")]
        [InlineData(@"
@@public
export type MyType = number;")]
        [InlineData(@"
export const zz = 42;
@@public
export {zz as z};")]
        public void PublicDecoratorIsAllowedForThisLocation(string code)
        {
            var diagnostics = GetDiagnostics(code);
            Assert.Empty(diagnostics);
        }

        [Theory]
        [InlineData(@"
@@public
namespace X {}")]
        [InlineData(@"
export const enum MyEnum { 
    @@public
    case1 
}")]
        [InlineData(@"
function g() {
    @@public
    export const x = 42;
}")]
        public void PublicDecoratorIsNotAllowedForThisLocation(string code)
        {
            var diagnostics = GetDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }

        [Fact]
        public void PublicDecoratorsHaveToBeExported()
        {
            var code = @"
@@public
const x = 42";

            var diagnostics = GetDiagnostics(code);
            Assert.Single(diagnostics);
        }

        [Fact]
        public void PublicDecoratorsOnlyAllowedInImplicitReferenceModule()
        {
            var code = @"
@@public
export const x = 42";

            var diagnostics = GetDiagnostics(code, implicitRefenceModule: false);

            Assert.Single(diagnostics);
        }

        private List<Diagnostic> GetDiagnostics(string code, bool implicitRefenceModule = true)
        {
            var diagnostics = TypeCheckingHelper.GetSemanticDiagnostics(
                parsingOptions: ParsingOptions,
                implicitReferenceModule: implicitRefenceModule,
                codes: code);

            foreach (var d in diagnostics)
            {
                m_output.WriteLine(d.ToString());
            }

            return diagnostics;
        }
    }
}
