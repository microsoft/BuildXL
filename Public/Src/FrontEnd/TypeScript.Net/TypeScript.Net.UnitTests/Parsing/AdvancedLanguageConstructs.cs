// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    /// <summary>
    /// Special test case that contains some complex language constructs.
    /// </summary>
    public class AdvancedLanguageConstructs
    {
        [Fact(Skip = "Pretty printing is not ready for this feature")]
        public void FunctionThatReturnsLambda()
        {
            string code =
@"function foo<T extends {x: number}>(y: number): (() => number) {
    return undefined;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact(Skip = "Pretty printing is not ready for this feature")]
        public void GenericArgumentThatExtendsCallSignature()
        {
            string code =
@"function boo<T extends () => void>(y: number): (() => number) {
    return undefined;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact(Skip = "Pretty printing is not ready for this feature")]
        public void CallSignature()
        {
            // Call signature that returns a call signature
            string code = @"let x1: {(): {()}} = undefined;";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ShortHandPropertyAssignment()
        {
            // Short-hand property assignment
            // c: {a: any; b: number};
            string code =
@"{
    let a, b = 1;
    let c = {
        a,
        b,
    };
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact(Skip = "Pretty printing is not implemented yet")]
        public void ArrayDestructuring()
        {
            string code = @"
{
    // x == 1, y == 2
    let [x, y = 0] = [1, 2];
    // x == 1, y == 3
    [x, y = 3] = [2]
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact(Skip = "Pretty printing is not implemented yet")]
        public void ObjectDestructuring()
        {
            string code = @"
{
    let obj = {x: 42, y: 36};
    let {x, y} = obj;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact(Skip = "Not implemented yet.")]
        public void ArgumentWithObjectBinding()
        {
            string code = @"
function ff({x: x1 = {}, y, z}) {
    // x1 is local that would be mapped from x property of the argument or will be {} if x is missing.
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void FunctionWithAutomaticSemicolonInsertionAtReturn()
        {
            string code =
@"function ff2() {
    return 
    {
        
    }
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);

            string expected =
@"function ff2() {
    return ;
    {
    }
}";
            Assert.Equal(expected, node.GetFormattedText());
        }

        [Fact]
        public void NamespaceWithNamespaceName()
        {
            string code =
@"namespace namespace {
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IModuleDeclaration>(code, roundTripTesting: false);

            Assert.Equal("namespace", node.Name.Text);
        }

        [Fact]
        public void ModuleWithModuleName()
        {
            string code =
@"module module {
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IModuleDeclaration>(code, roundTripTesting: false);

            Assert.Equal("module", node.Name.Text);
        }

        [Fact]
        public void CommaOperatorSeparateFromAssignment()
        {
            string code =
@"{
    let aa;
    aa = 1 , 2 , 3;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void CommaOperatorInAssignmentWithWeirdExpressionsInside()
        {
            // z is a string
            string code =
@"let z = (1 , (() => 1)() , ""3"")";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void IfStatementWithSingleLineBody()
        {
            string code =
@"{
    let z = 0;
    if (z) return true;
    return false;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);

            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void LambdaApplicationInCondition()
        {
            string code =
@"{
    let test = true;
    let x = (test) ? ( () => {
        if (test) return [];
    }
    )() : [];
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);

            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
