// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using TypeScript.Net.Utilities;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class LineInformation
    {
        [Fact]
        public void TestLineInfo()
        {
            string code =
@"namespace X {
    enum Foo {value = 42}
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var moduleDeclaration = (IModuleDeclaration)sourceFile.Statements[0];
            var @enum = (IEnumDeclaration)moduleDeclaration.Body.Statements[0];

            var lineAndColumn = @enum.GetLineInfo(sourceFile);
            Assert.Equal(2, lineAndColumn.Line);
            Assert.Equal(5, lineAndColumn.Position);
        }

        [Fact]
        public void TestNodeAfterOpenBrace()
        {
            string code =
@"function foo(): number {
    let x = 1;
    return x + 1;
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var foo = (IFunctionDeclaration)sourceFile.Statements[0];
            var @let = foo.Body.Statements[0];
            var @return = foo.Body.Statements[1];

            var fooLineInfo = foo.GetLineInfo(sourceFile);
            var letLineInfo = @let.GetLineInfo(sourceFile);
            var returnLineInfo = @return.GetLineInfo(sourceFile);

            Assert.Equal(1, fooLineInfo.Line);
            Assert.Equal(2, letLineInfo.Line);
            Assert.Equal(3, returnLineInfo.Line);
        }

        [Fact]
        public void TestNodeAfterOpenBrace2()
        {
            string code =
@"namespace X {
    function foo(): number {
       return 1;
    }
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var moduleDeclaration = (IModuleDeclaration)sourceFile.Statements[0];
            var foo = (IFunctionDeclaration)moduleDeclaration.Body.Statements[0];
            var @return = foo.Body.Statements[0];

            var fooLineInfo = foo.GetLineInfo(sourceFile);
            var returnLineInfo = @return.GetLineInfo(sourceFile);

            Assert.Equal(2, fooLineInfo.Line);
            Assert.Equal(3, returnLineInfo.Line);
        }

        [Fact]
        public void TestNodeAfterBlockStatement()
        {
            string code =
@"function foo(): number {
    for (let i = 0; i < 1; i++) {
        Console.writeLine(i);
    }

    return 1;
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var foo = (IFunctionDeclaration)sourceFile.Statements[0];
            var @for = foo.Body.Statements[0];
            var @return = foo.Body.Statements[1];

            var fooLineInfo = foo.GetLineInfo(sourceFile);
            var forLineInfo = @for.GetLineInfo(sourceFile);
            var returnLineInfo = @return.GetLineInfo(sourceFile);

            Assert.Equal(1, fooLineInfo.Line);
            Assert.Equal(2, forLineInfo.Line);
            Assert.Equal(6, returnLineInfo.Line);
        }

        [Fact]
        public void TestNodeAfterLineComment()
        {
            string code =
@"function foo(): number {
    let a = 1; // comment
    return a + 1;
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var foo = (IFunctionDeclaration)sourceFile.Statements[0];
            var @let = foo.Body.Statements[0];
            var @return = foo.Body.Statements[1];

            var fooLineInfo = foo.GetLineInfo(sourceFile);
            var letLineInfo = @let.GetLineInfo(sourceFile);
            var returnLineInfo = @return.GetLineInfo(sourceFile);

            Assert.Equal(1, fooLineInfo.Line);
            Assert.Equal(2, letLineInfo.Line);
            Assert.Equal(3, returnLineInfo.Line);
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(1, 0, 0, 0)]
        [InlineData(0, 1, 0, 0)]
        [InlineData(0, 0, 1, 0)]
        [InlineData(0, 0, 0, 1)]
        [InlineData(2, 0, 0, 2)]
        [InlineData(1, 0, 0, 1)]
        public void TestNodeAfterMultilineComment(int preCommentStart, int postCommentStart, int preCommentEnd, int postCommentEnd)
        {
            Func<int, string> makeNewLines = (n) => string.Join(string.Empty, Enumerable.Repeat(Environment.NewLine, n));

            string code =
@"function foo(): number {
    let a = 1; " + makeNewLines(preCommentStart) + "/*" + makeNewLines(postCommentStart) + @"
        comment " + makeNewLines(preCommentEnd) + "*/" + makeNewLines(postCommentEnd) + @"
    return a + 1;
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var foo = (IFunctionDeclaration)sourceFile.Statements[0];
            var @let = foo.Body.Statements[0];
            var @return = foo.Body.Statements[1];

            var fooLineInfo = foo.GetLineInfo(sourceFile);
            var letLineInfo = @let.GetLineInfo(sourceFile);
            var returnLineInfo = @return.GetLineInfo(sourceFile);

            Assert.Equal(1, fooLineInfo.Line);
            Assert.Equal(2, letLineInfo.Line);
            Assert.Equal(4 + preCommentStart + postCommentStart + preCommentEnd + postCommentEnd, returnLineInfo.Line);
        }

        [Fact]
        public void TestNodeAfterSingleAndMultilineComment()
        {
            Func<int, string> makeNewLines = (n) => string.Join(string.Empty, Enumerable.Repeat(Environment.NewLine, n));

            string code =
@"function foo(): number {
    let a = 1; 
    /** blah */

    // return
    return a + 1;
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var foo = (IFunctionDeclaration)sourceFile.Statements[0];
            var @let = foo.Body.Statements[0];
            var @return = foo.Body.Statements[1];

            var fooLineInfo = foo.GetLineInfo(sourceFile);
            var letLineInfo = @let.GetLineInfo(sourceFile);
            var returnLineInfo = @return.GetLineInfo(sourceFile);

            Assert.Equal(1, fooLineInfo.Line);
            Assert.Equal(2, letLineInfo.Line);
            Assert.Equal(6, returnLineInfo.Line);
        }

        [Fact]
        public void TestNodeAfterStatement()
        {
            string code =
@"function foo(): number {
    let a = 1;return a + 1;
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var foo = (IFunctionDeclaration)sourceFile.Statements[0];
            var @let = foo.Body.Statements[0];
            var @return = foo.Body.Statements[1];

            var fooLineInfo = foo.GetLineInfo(sourceFile);
            var letLineInfo = @let.GetLineInfo(sourceFile);
            var returnLineInfo = @return.GetLineInfo(sourceFile);

            Assert.Equal(1, fooLineInfo.Line);
            Assert.Equal(2, letLineInfo.Line);
            Assert.Equal(5, letLineInfo.Position);
            Assert.Equal(2, returnLineInfo.Line);
            Assert.Equal(15, returnLineInfo.Position);
        }
    }
}
