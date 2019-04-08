// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class FunctionDeclarations
    {
        [Fact]
        public void FunctionWithoutArgumentsAndReturnsNothing()
        {
            string code =
@"export function x() {
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void FunctionWith3ArgumentsAndReturnType()
        {
            string code =
@"function x(s: string, f: Foo, a: any) : boolean {
    return false;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void FunctionWithInterfaceDefinitionsInplace()
        {
            string code =
@"function x(s: string, f: {x: number}, a: any) : {y: number} {
    return undefined;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SimpleFunctionWithLetStatemnt()
        {
            string code =
@"function foo() : number {
    let x = 42;
    return x;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void SimpleFunctionWithVariableReAssignment()
        {
            string code =
@"function foo() : number {
    let x = 42;
    x = 24;
    return x;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void GenericFunctionWithT()
        {
            string code =
@"function foo<T>(t: T) : T {
    return t;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void GenericFunctionWithUThatExtendsT()
        {
            // TODO: this code is parsed correctly.
            // PrettyPrinter is not fully implemented
            string code =
@"function foo<T, U extends T>(t: T): U {
    return <U>t;
}";

            string expectedTemporary =
@"function foo<T, U>(t: T) : U {
    return <U>t;
}";
            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);
            Assert.Equal(expectedTemporary, node.GetFormattedText());
        }

        [Fact]
        public void GenericFunctionWithGenericThatExtendsTypeLiteral()
        {
            // TODO: this code is parsed correctly.
            // PrettyPrinter is not fully implemented
            string code =
@"function foo<T extends {x: number}>(t: T): T {
    return t;
}";

            string expectedTemporary =
@"function foo<T>(t: T) : T {
    return t;
}";
            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);
            Assert.Equal(expectedTemporary, node.GetFormattedText());
        }

        [Fact]
        public void FunctionWithIfStatementAndComplexReturn()
        {
            string code =
@"function createArtifact(value: File, kind: ArtifactKind, original?: File) : Artifact {
    if (value === undefined) {
        return undefined;
    } else {
        console.writeLine(42);
    }
    return <Artifact>{
        path: value,
        kind: kind,
        original: original,
    };
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IFunctionDeclaration>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ExpressionBasedArrowFunction()
        {
            string code =
@"{
    let x: int[] = [1, 2, 3];
    let y: string[] = x.map(n => n.toString());
}";
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void ArrowFunctionWithBody()
        {
            string code =
@"let func: (n: number, s: string) => {n: number, s: string} = (n: number, s: string) => {
    return {
        n,
        s,
    };
}";
            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
