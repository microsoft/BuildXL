// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.DScript;
using TypeScript.Net.UnitTests.TypeChecking;
using Xunit;

namespace Test.DScript.TypeChecking
{
    public sealed class EscapeIdentifiersTests
    {
        private const string BrandingAssignment =
@"interface One {
    __brand: any;
}
 
interface Two {
    __brand2: any;
}
 
const one: One = undefined;
const two: Two = one;";

        [Fact]
        public void IdentifiersAreEscaped()
        {
            var diagnostics = TypeCheckingHelper.GetSemanticDiagnostics(
                parsingOptions: ParsingOptions.GetDefaultParsingOptionsWithEscapeIdentifiers(true),
                implicitReferenceModule: true,
                codes: BrandingAssignment);
            Assert.Single(diagnostics);
        }

        [Fact]
        public void IdentifiersAreNotEscapedIfEscapingIsTurnedOff()
        {
            var diagnostics = TypeCheckingHelper.GetSemanticDiagnostics(
                parsingOptions: ParsingOptions.GetDefaultParsingOptionsWithEscapeIdentifiers(false),
                implicitReferenceModule: true,
                codes: BrandingAssignment);
            Assert.Empty(diagnostics);
        }
    }
}
