// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Incrementality;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Sdk;
using Test.BuildXL.TestUtilities.Xunit;
using TypeScript.Net.Parsing;
using TypeScript.Net.TypeChecking;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.TypeChecking;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace Test.DScript.TypeChecking
{
    public sealed class PublicSurfaceCheckerTests
    {
        private readonly PathTable m_pathTable = new PathTable();

        [Theory]
        [InlineData("export const result = 42;")]
        [InlineData("export const y = true, result = 42, z = 'hi';")]
        [InlineData("export enum result {}")]
        [InlineData("export function result() {}")]
        [InlineData("namespace result {}")]
        [InlineData("export const x = 42; export {x as result};")]
        public void TypeCheckerSeesOriginalPositionsForValues(string specContent)
        {
            TypeCheckerSeesOriginalPositions(specContent, "export const x = result;", "x");
        }

        [Theory]
        [InlineData("export type result = number;")]
        [InlineData("export interface result {}")]
        public void TypeCheckerSeesOriginalPositionsForTypes(string specContent)
        {
            TypeCheckerSeesOriginalPositions(specContent, "export type x = result;", "x");
        }

        [Fact]
        public void TypeCheckerSeesOriginalPositionsForNestedNamespace()
        {
            TypeCheckerSeesOriginalPositions("namespace A.B.C {}", "export const x = A.B;", "x");
        }

        [Fact]
        public void TypeCheckerSeesOriginalPositionsForEnumMembers()
        {
            TypeCheckerSeesOriginalPositions("export enum E {One, Two}", "export const x = E.One;", "x");
        }

        #region helpers
        private void TypeCheckerSeesOriginalPositions(string specContent, string referenceContent, string referenceName)
        {
            var referenceFile = ParsingHelper.ParseSourceFile(referenceContent);

            var originalFile = ParsingHelper.ParseSourceFile(specContent);
            var checker = GetCheckerForFiles(referenceFile, originalFile);

            var publicContent = GetPublicSurface(originalFile, checker);
            var publicFile = ParsingHelper.ParseSourceFile(code: publicContent.GetContentAsString(), parser: new PublicSurfaceParser(m_pathTable, new byte[]{42}, 1));
            var publicChecker = GetCheckerForFiles(referenceFile, publicFile);

            var reference = GetReference(referenceFile, referenceName);

            var originalSymbol = checker.GetSymbolAtLocation(reference);
            var publicSymbol = publicChecker.GetSymbolAtLocation(reference);

            XAssert.AreEqual(originalSymbol.DeclarationList[0].Pos, publicSymbol.DeclarationList[0].Pos);
        }

        private static FileContent GetPublicSurface(ISourceFile originalFile, ITypeChecker checker)
        {
            using (var writer = new ScriptWriter())
            {
                var printer = new PublicSurfacePrinter(writer, new SemanticModel(checker));

                FileContent publicContent;
                var result = printer.TryPrintPublicSurface(originalFile, out publicContent);

                XAssert.IsTrue(result);
                return publicContent;
            }
        }

        private static ITypeChecker GetCheckerForFiles(params ISourceFile[] originalFile)
        {
            var host = new TypeCheckerHostFake(new ModuleName("MyModule", projectReferencesAreImplicit: true), originalFile);
            return Checker.CreateTypeChecker(host, true, degreeOfParallelism: 1);
        }

        private static INode GetReference(ISourceFile spec, string identifierName)
        {
            var firstIdentifier = NodeWalker.ForEachChildRecursively(
                spec,
                node =>
                {
                    var identifier = node.As<IIdentifier>();
                    if (identifier?.Text == identifierName)
                    {
                        return identifier;
                    }
                    return null;
                });

            return firstIdentifier;
        }
#endregion
    }
}
