// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using Xunit;

namespace TypeScript.Net.UnitTests.Factory
{
    public class SwitchStatementTests
    {
        [Fact]
        public void SwitchOnlyExpression()
        {
            var node = new SwitchStatement(new Identifier("caseExpr"));
            Assert.Equal(
@"switch (caseExpr) {
}",
                node.GetFormattedText());
        }

        [Fact]
        public void SwitchOnlyDefault()
        {
            var node = new SwitchStatement(
                new Identifier("caseExpr"),
                new DefaultClause(new ReturnStatement(new Identifier("valueDefault"))));
            Assert.Equal(
@"switch (caseExpr) {
    default:
        return valueDefault;
}",
                node.GetFormattedText());
        }

        [Fact]
        public void SwitchOnlyWithTwoCasesAndFallThrough()
        {
            var node = new SwitchStatement(
                new Identifier("caseExpr"),
                new DefaultClause(new ReturnStatement(new Identifier("valueDefault"))),
                new CaseClause(new LiteralExpression("case1"), new ReturnStatement(new Identifier("valueCase1"))),
                new CaseClause(new LiteralExpression("case2fall")),
                new CaseClause(new LiteralExpression("case2"), new ReturnStatement(new Identifier("valueCase2AndFall"))),
                new CaseClause(new LiteralExpression("case3block"), new Block(new ReturnStatement(new Identifier("valueCase3")))));
            Assert.Equal(
@"switch (caseExpr) {
    case ""case1"":
        return valueCase1;
    case ""case2fall"":
    case ""case2"":
        return valueCase2AndFall;
    case ""case3block"":
        {
            return valueCase3;
        }
    default:
        return valueDefault;
}",
                node.GetFormattedText());
        }
    }
}
