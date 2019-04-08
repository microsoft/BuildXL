// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.DScript;
using TypeScript.Net.Reformatter;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace Test.DScript.Reformatter
{
    public sealed class TriviaPreservingTests
    {
        private static readonly ParsingOptions s_preserveTrivia = new ParsingOptions(
            namespacesAreAutomaticallyExported: false,
            generateWithQualifierFunctionForEveryNamespace: false,
            preserveTrivia: true,
            allowBackslashesInPathInterpolation: true,
            useSpecPublicFacadeAndAstWhenAvailable: false,
            escapeIdentifiers: true);

        [Fact]
        public void TopLevelStatementsPreserveNewLine()
        {
            string code =
@"export const x1 = 42;

export const x2 = 42;

export const x3 = 42;";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void TopLevelOrNamespaceStatementsPreserveNewLine()
        {
            string code =
@"export const x1 = 42;

namespace A {
    
    export const x2 = 42;
    
    export const x3 = 42;
}

export const x4 = 42;";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void ArrayLiteralElementsPreserveNewLine()
        {
            string code =
@"export const x1 = [
    1,
    
    2,
    3,
    
    4,
    5,
    
    6,
    7,
];";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void ObjectLiteralElementsPreserveNewLine()
        {
            string code =
                @"export const x1 = {
    a: 1,
    b: 2,
    
    c: 3,
    
    d: 4,
};";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void EnumElementsPreserveNewLine()
        {
            string code =
                @"export const enum E {
    a,
    b,
    
    c,
    
    d,
}";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void BlockStatementsPreserveNewLine()
        {
            string code =
@"{
    
    export const x1 = 42;
    
    export const x2 = 42;
    
    export const x3 = 42;
}";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void CommentsArePreservedInTopLevelStatements()
        {
            string code =
@"// A comment
export const x1 = 42;

/* A multiline
comment */
export const x2 = 42;

// Another comment
export const x3 = 42;";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void CommentsArePreservedInNamespaceStatements()
        {
            string code =
@"namespace N {
    // A comment
    export const x1 = 42;
    
    /* A multiline
    comment */
    export const x2 = 42;
}";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void CommentsArePreservedInObjectLiteralMembers()
        {
            string code =
@"const x = {
    // A comment
    a: 1,
    
    /* A multiline 
    comment */
    b: 2,
};";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void CommentsArePreservedInArrayMembers()
        {
            string code =
@"export const x = [
    // A comment
    1,
    /* A multiline 
    comment */
    2,
];";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void CommentsArePreservedInInterfaceMembers()
        {
            string code =
@"export interface I {
    // A comment
    a: string;
    /* A multiline 
    comment */
    b: number;
}";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void CommentsArePreservedInEnumMembers()
        {
            string code =
@"export const enum E {
    /** A comment. */
    a,
    // Another comment
    b,
}";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void SameLineCommentIsNotPreserved()
        {
            string code =
@"export const x = 42; // This comment is not preserved here";

            var node = ParsingHelper.ParseSourceFile(code, parsingOptions: s_preserveTrivia);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }
    }
}
