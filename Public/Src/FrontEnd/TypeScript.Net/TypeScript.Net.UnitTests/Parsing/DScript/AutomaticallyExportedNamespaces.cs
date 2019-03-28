// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public sealed class AutomaticallyExportedNamespaces
    {
        private static readonly ParsingOptions ParsingOptions = new ParsingOptions(
            namespacesAreAutomaticallyExported: true,
            generateWithQualifierFunctionForEveryNamespace: false,
            preserveTrivia: false,
            allowBackslashesInPathInterpolation: true,
            useSpecPublicFacadeAndAstWhenAvailable: false,
            escapeIdentifiers: true);

        private static void ParseModuleAndAssertFlag(string code, Visibility visibility)
        {
            var node = ParsingHelper.ParseSourceFile(code, "FakeFileName.dsc", ParsingOptions);
            var module = (IModuleDeclaration)node.Statements.First();

            Assert.Equal(visibility, GetVisibility());

            Visibility GetVisibility()
            {
                if ((module.Flags & NodeFlags.ScriptPublic) != NodeFlags.None)
                {
                    return Visibility.Public;
                }

                if ((module.Flags & NodeFlags.Export) != NodeFlags.None)
                {
                    return Visibility.Export;
                }

                return Visibility.None;
            }
        }

        public enum Visibility
        {
            None,
            Export,
            Public,
        }

        // The namespace visibility is always public, regardless of their content.

        [Theory]
        [InlineData(@"export const x: number = 42;", Visibility.Public)]
        [InlineData(@"export interface I{}", Visibility.Public)]
        [InlineData(@"export function g(){}", Visibility.Public)]
        [InlineData(
            @"
@@public
export const x: number = 42;", Visibility.Public)]
        [InlineData(
            @"
@@public
export interface I{}", Visibility.Public)]
        [InlineData(
            @"
@@public
export function g(){}", Visibility.Public)]
        public void NamespacesAreExportedOrPublicWhenAMemberIsExportedOrPublic(string member, Visibility visibility)
        {
            string code = @"namespace X {" + member + "}";

            ParseModuleAndAssertFlag(code, visibility);
        }

        [Theory]
        [InlineData(
            @"
namespace X {
    export const x = 32;
    const y = 33;
}", Visibility.Public)]
        [InlineData(
            @"
namespace X {
    @@public    
    export const x = 32;
    const y = 33;
}", Visibility.Public)]
        public void NamespacesAreExportedOrPublicWhenAnyMemberIsExportedOrPublic(string code, Visibility visibility)
        {
            ParseModuleAndAssertFlag(code, visibility);
        }

        [Theory]
        [InlineData(
            @"
namespace X {
    namespace Y {
        export const x = 32;
        const y = 33;
   }
}", Visibility.Public)]
        [InlineData(
            @"
namespace X {
    namespace Y {
        @@public
        export const x = 32;
        const y = 33;
   }
}", Visibility.Public)]
        public void NonTopLevelNamespaceIsExportedOrPublicWhenAMemberIsExportedOrPublic(string code, Visibility visibility)
        {
            ParseModuleAndAssertFlag(code, visibility);
        }
    }
}
