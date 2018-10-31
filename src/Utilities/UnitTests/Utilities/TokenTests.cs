// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.Utilities
{
    public sealed class TokenTests
    {
        [Fact]
        public void TestTokenRenumberOfEmpty()
        {
            var tokenTextTable = new TokenTextTable();
            var pathTable = new PathTable();
            AbsolutePath path = AbsolutePath.Create(pathTable, A("t","a.txt"));
            var token = new Token(path, 0, 0, TokenText.Create(tokenTextTable, string.Empty));
            Token newToken = token.UpdateLineInformationForPosition(tokenTextTable, 0);
            XAssert.AreEqual(path, newToken.Path);
            XAssert.AreEqual(0, newToken.Line);
            XAssert.AreEqual(0, newToken.Position);
            XAssert.AreEqual(string.Empty, token.Text.ToString(tokenTextTable));
        }

        [Fact]
        public void TestTokenRenumberOfNonemptyBy0()
        {
            var tokenTextTable = new TokenTextTable();
            var pathTable = new PathTable();
            AbsolutePath path = AbsolutePath.Create(pathTable, A("t","a.txt"));
            var token = new Token(path, 100, 100, TokenText.Create(tokenTextTable, "abc"));
            Token newToken = token.UpdateLineInformationForPosition(tokenTextTable, 0);
            XAssert.AreEqual(path, newToken.Path);
            XAssert.AreEqual(100, newToken.Line);
            XAssert.AreEqual(100, newToken.Position);
            XAssert.AreEqual("abc", token.Text.ToString(tokenTextTable));
        }

        [Fact]
        public void TestTokenRenumberOfNonemptyBy1WithoutNewLine()
        {
            var tokenTextTable = new TokenTextTable();
            var pathTable = new PathTable();
            AbsolutePath path = AbsolutePath.Create(pathTable, A("t", "a.txt"));
            var token = new Token(path, 100, 100, TokenText.Create(tokenTextTable, "abc"));
            Token newToken = token.UpdateLineInformationForPosition(tokenTextTable, 1);
            XAssert.AreEqual(path, newToken.Path);
            XAssert.AreEqual(100, newToken.Line);
            XAssert.AreEqual(101, newToken.Position);
            XAssert.AreEqual("abc", token.Text.ToString(tokenTextTable));
        }

        [Fact]
        public void TestTokenRenumberOfNonemptyBy3WithoutNewLine()
        {
            var tokenTextTable = new TokenTextTable();
            var pathTable = new PathTable();
            AbsolutePath path = AbsolutePath.Create(pathTable, A("t", "a.txt"));
            var token = new Token(path, 100, 100, TokenText.Create(tokenTextTable, "abc"));
            Token newToken = token.UpdateLineInformationForPosition(tokenTextTable, 3);
            XAssert.AreEqual(path, newToken.Path);
            XAssert.AreEqual(100, newToken.Line);
            XAssert.AreEqual(102, newToken.Position);
            XAssert.AreEqual("abc", token.Text.ToString(tokenTextTable));
        }

        [Fact]
        public void TestTokenRenumberOfBigStringWithMultipleLines()
        {
            var tokenTextTable = new TokenTextTable();
            var pathTable = new PathTable();
            string txt = @"abc
de
f
ghi";
            AbsolutePath path = AbsolutePath.Create(pathTable, A("t", "a.txt"));
            var token = new Token(path, 100, 100, TokenText.Create(tokenTextTable, txt));
            Token newToken = token.UpdateLineInformationForPosition(tokenTextTable, 13);
            XAssert.AreEqual('h', txt[13]);
            XAssert.AreEqual(path, newToken.Path);
            XAssert.AreEqual(103, newToken.Line);
            XAssert.AreEqual(1, newToken.Position);
        }
    }
}
