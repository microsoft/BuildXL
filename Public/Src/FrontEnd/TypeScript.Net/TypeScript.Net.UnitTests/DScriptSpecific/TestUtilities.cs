// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using TypeScript.Net.DScript;
using TypeScript.Net.UnitTests.TypeChecking;
using Xunit;

namespace Test.DScript.DScriptSpecific
{
    /// <nodoc/>
    public static class TestUtilities
    {
        /// <summary>
        /// Default parsing options for testing
        /// </summary>
        public static readonly ParsingOptions DefaultParsingOptions = new ParsingOptions(
            namespacesAreAutomaticallyExported: true,
            generateWithQualifierFunctionForEveryNamespace: true,
            preserveTrivia: false,
            allowBackslashesInPathInterpolation: true,
            useSpecPublicFacadeAndAstWhenAvailable: false,
            escapeIdentifiers: true);

        /// <summary>
        /// Type checks all <param name="codes"/> as part of the same module with default parsing options and asserts no errors are present
        /// </summary>
        public static void TypeCheckAndAssertNoErrors(this string[] codes)
        {
            var diagnostics = TypeCheckingHelper.GetSemanticDiagnostics(
                parsingOptions: DefaultParsingOptions,
                implicitReferenceModule: true, codes: codes);
            Assert.Empty(diagnostics);
        }

        /// <summary>
        /// Type checks <param name="code"/> with default parsing options and asserts no errors are present
        /// </summary>
        public static void TypeCheckAndAssertNoErrors(this string code)
        {
            TypeCheckAndAssertNoErrors(new[] { code });
        }

        /// <summary>
        /// Type checks all <param name="codes"/> as part of the same module with default parsing options and asserts there is a single
        /// outcoming error that contains substring <param name="messageSubstring"/>
        /// </summary>
        public static void TypeCheckAndAssertSingleError(this string[] codes, string messageSubstring)
        {
            var diagnostics = TypeCheckingHelper.GetSemanticDiagnostics(
                parsingOptions: DefaultParsingOptions,
                implicitReferenceModule: true, codes: codes);
            Assert.Equal(1, diagnostics.Count);
            Assert.True(diagnostics.First().MessageText.ToString().Contains(messageSubstring));
        }

        /// <summary>
        /// Type checks all <param name="code"/> with default parsing options and asserts there is a single
        /// outcoming error that contains substring <param name="messageSubstring"/>
        /// </summary>
        public static void TypeCheckAndAssertSingleError(this string code, string messageSubstring)
        {
            TypeCheckAndAssertSingleError(new[] { code }, messageSubstring);
        }
    }
}
