// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class VariableDeclarations
    {
        [Fact]
        public void ConstBinding()
        {
            string code =
@"const x = 42";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ConstBindingIsConstant()
        {
            string code =
@"const x = 42";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.True(NodeUtilities.IsConst(node));
        }

        [Fact]
        public void VarBindingIsNotConstant()
        {
            string code =
@"var x = 42";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.False(NodeUtilities.IsConst(node));
        }

        [Fact]
        public void ConstBindingWithType()
        {
            string code =
@"const x: number = 42";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void LetBindingWithTypeLiteral()
        {
            string code =
@"let x: {y: number} = undefined";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void LetBindingWithType()
        {
            string code =
@"export let x: number = 42";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void VarBindingWithType()
        {
            string code =
@"export var x: number = 42;";

            var node = ParsingHelper.ParseSourceFile(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ExportConstBindingWithCustomType()
        {
            string code =
@"export const x: MyType = {}";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ObjectLiteralExpression()
        {
            string code =
@"let x = {x: 42}";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ConstBindingWithMultipleVariables()
        {
            string code =
@"const x, y = 42";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SimpleFunctionInvocation()
        {
            string code =
@"{
    export function foo() {return 42;}
    let x: number = foo();
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Console.WriteLine(node.GetFormattedText());
        }
    }
}
