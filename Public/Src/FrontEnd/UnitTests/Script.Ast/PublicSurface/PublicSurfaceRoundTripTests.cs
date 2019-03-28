// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using Xunit;

namespace Test.DScript.Ast.PublicSurface
{
    public class PublicSurfaceRoundTripTests : PublicSurfaceTests
    {
        [Theory]
        [InlineData("export const x = 42;")]
        [InlineData("export function g() { return 42 };")]
        [InlineData("export interface I { a : string }")]
        [InlineData("namespace A {}")]
        [InlineData("export type T = boolean;")]
        [InlineData("export enum E {}")]
        [InlineData("export enum E { One, Two }")]
        [InlineData("export const x = 42, y = true, z = 'hi';")]
        [InlineData("const x = 42; export {x};")]
        [InlineData("const x = 42; export {x, x as y};")]
        public void PositionsForTopLevelStatementsArePreserved(string spec)
        {
            PositionIsTheSameForAllPublicDeclarations(spec);
        }

        [Theory]
        [InlineData(@"
export const x = 42;
const y = 33;

namespace A.B.C {
    export interface I {
        a: string;
        b: number;
    }

    export function g (a: number) {
        return 42;
    }

    export const z = 42, k = true;
    const w = 55;

    export enum E {
        One,
        Two
    }
}
export {y as k, y as t};
")]
        public void PositionsForNestedStatementsArePreserved(string spec)
        {
            PositionIsTheSameForAllPublicDeclarations(spec);
        }

        #region helpers

        private void PositionIsTheSameForAllPublicDeclarations(string spec)
        {
            var publicVersion = GetPublicSurface(spec);

            var publicParser = new PublicSurfaceParser(PathTable, new byte[] {42}, 1);
            var publicSource = publicParser.ParseSourceFile(
                "fake.dsc",
                publicVersion,
                ScriptTarget.Es2015,
                syntaxCursor: null,
                setParentNodes: true,
                parsingOptions: ParsingOptions.DefaultParsingOptions);

            var parser = new Parser();
            var originalSource = parser.ParseSourceFile(
                "fake.dsc",
                spec,
                ScriptTarget.Es2015,
                syntaxCursor: null,
                setParentNodes: true,
                parsingOptions: ParsingOptions.DefaultParsingOptions);

            var originalDeclarations = CollectAllDeclarationsWithPositions(originalSource);
            var publicDeclarations = CollectAllDeclarationsWithPositions(publicSource);

            foreach (var key in publicDeclarations.Keys)
            {
                Assert.Equal(originalDeclarations[key], publicDeclarations[key]);
            }
        }

        /// <summary>
        /// Assumes declaration names are unique (full names are not used)
        /// </summary>
        private static Dictionary<string, int> CollectAllDeclarationsWithPositions(ISourceFile sourceFile)
        {
            var identifersToPositions = new Dictionary<string, int>();

            NodeWalker.ForEachChildRecursively<INode>(sourceFile,
                node =>
                {
                    var declarationStatement = node.As<IDeclarationStatement>();
                    if (declarationStatement?.Name != null)
                    {
                        identifersToPositions.Add(declarationStatement.Name.Text + declarationStatement.Kind, declarationStatement.Pos);
                    }

                    var variableStatement = node.As<IVariableStatement>();
                    if (variableStatement != null)
                    {
                        foreach (var declaration in variableStatement.DeclarationList.Declarations)
                        {
                            identifersToPositions.Add(declaration.Name.Cast<IIdentifier>().Text + declaration.Kind, declaration.Pos);
                        }
                    }

                    var enumMember = node.As<IEnumMember>();
                    if (enumMember != null)
                    {
                        identifersToPositions.Add(enumMember.Name.Text + enumMember.Kind, enumMember.Pos);
                    }

                    var exportSpecifier = node.As<IExportSpecifier>();
                    if (exportSpecifier != null)
                    {
                        identifersToPositions.Add(exportSpecifier.Name.Text + exportSpecifier.Kind, exportSpecifier.Pos);
                    }

                    return null;
                });

            return identifersToPositions;
        }
    }
#endregion
}
