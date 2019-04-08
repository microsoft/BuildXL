// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public sealed class PublicSurfaceParserTests
    {
        private readonly PathTable m_pathTable = new PathTable();

        [Theory]
        [InlineData(
            @"
@@42
export declare const x: number;
", 42)]
        [InlineData(
            @"
@@42
export enum X {}
", 42)]
        [InlineData(
            @"
@@42
export declare function f() : void;
", 42)]
        [InlineData(
            @"
@@42
export interface I {}
", 42)]
        [InlineData(
            @"
@@42
export type T = number;
", 42)]
        [InlineData(
            @"
@@42
namespace A {}
", 42)]
        [InlineData(
            @"
@@42
export type T = number;
", 42)]
        public void OriginalPositionIsPreservedForDeclarations(string spec, int expectedPosition)
        {
            var position = ParseSingleDeclarationAndGetPosition(spec);
            Assert.Equal(expectedPosition, position);
        }

        [Fact]
        public void OriginalPositionIsPreservedForNestedNamespaceDeclaration()
        {
            var spec = @"
@@42
namespace Windows {
    @@53
    namespace Native {
        @@64
        namespace Shell { 
        }
    }
}";

            var moduleDeclaration = ParseStatement(spec, 0).Cast<IModuleDeclaration>();
            Assert.Equal(42, moduleDeclaration.Pos);

            var nestedModuleDeclaration = moduleDeclaration.Body.AsModuleBlock().Statements[0];
            Assert.Equal(53, nestedModuleDeclaration.Pos);

            var nestedNestedModuleDeclaration = nestedModuleDeclaration.Cast<IModuleDeclaration>().Body.AsModuleBlock().Statements[0];
            Assert.Equal(64, nestedNestedModuleDeclaration.Pos);
        }

        [Fact]
        public void OriginalPositionIsPreservedForMultipleStatements()
        {
            var spec = @"
@@100
export declare const x: number;

@@200
export interface I {a :string}

@@300
namespace A {
    @@400
    export type T = number;

    @@500
    @@public
    export declare function f(): number;

    @@600
    namespace B{
        @@700
        export declare const y: boolean;
    }
}
";
            var sourceFile = ParseSourceFile(spec);
            Assert.Equal(100, GetDeclaration(sourceFile.Statements[0]).Pos);
            Assert.Equal(200, GetDeclaration(sourceFile.Statements[1]).Pos);
            Assert.Equal(300, GetDeclaration(sourceFile.Statements[2]).Pos);

            var module = sourceFile.Statements[2].As<IModuleDeclaration>();
            Assert.Equal(400, GetDeclaration(module.Body.AsModuleBlock().Statements[0]).Pos);
            Assert.Equal(500, GetDeclaration(module.Body.AsModuleBlock().Statements[1]).Pos);
            Assert.Equal(600, GetDeclaration(module.Body.AsModuleBlock().Statements[2]).Pos);

            var nestedModule = module.Body.AsModuleBlock().Statements[2].As<IModuleDeclaration>();
            Assert.Equal(700, GetDeclaration(nestedModule.Body.AsModuleBlock().Statements[0]).Pos);
        }

        #region helpers

        private int ParseSingleDeclarationAndGetPosition(string spec)
        {
            return ParseDeclarationAndGetIt(spec, 0).Pos;
        }

        private IDeclaration ParseDeclarationAndGetIt(string spec, int statementIndex)
        {
            var statement = ParseStatement(spec, statementIndex);

            return GetDeclaration(statement);
        }

        private static IDeclaration GetDeclaration(IStatement statement)
        {
            var declarationStatement = statement.As<IDeclarationStatement>();
            if (declarationStatement != null)
            {
                return declarationStatement;
            }

            var variableStatement = statement.As<IVariableStatement>();
            if (variableStatement != null)
            {
                Assert.Equal(1, variableStatement.DeclarationList.Declarations.Count);
                var declaration = variableStatement.DeclarationList.Declarations[0];
                return declaration;
            }

            Assert.True(false);
            return null;
        }

        private IStatement ParseStatement(string spec, int statementIndex)
        {
            var sourceFile = ParseSourceFile(spec);
            Assert.True(statementIndex < sourceFile.Statements?.Count);

            var statement = sourceFile.Statements[statementIndex];

            return statement;
        }

        private ISourceFile ParseSourceFile(string spec)
        {
            var sourceFile = ParsingHelper.ParseSourceFile(
                spec,
                "FakeFileName.dsc",
                ParsingOptions.DefaultParsingOptions,
                new PublicSurfaceParser(m_pathTable, new byte[] {42}, 1));

            Assert.NotEqual(null, sourceFile.Statements);

            return sourceFile;
        }

        #endregion
    }
}
