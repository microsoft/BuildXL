// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.PrettyPrinter
{
    public class PrettyPrinterStatementTests : DsTest
    {
        public PrettyPrinterStatementTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void VarUntyped()
        {
            TestStatement("let a = 1;");
        }

        [Fact]
        public void VarTyped1()
        {
            TestStatement("let a : string = \"a\";");
        }

        [Fact]
        public void VarTyped2()
        {
            TestStatement("let a : {f: number} = {f: 1};");
        }

        [Fact]
        public void IfThenAssignmentElseNone()
        {
            // In TypeScript locals could be defined only in block.
            // So this code is invalid: if (true) const a = 42;
            TestStatement(
                "let a = 0; if (a > 0) a = 1;",
                expected:
@"let a = 0;
    if (a > 0) 
        a = 1;");
        }

        [Fact]
        public void IfThenAssignmentElseAssignment()
        {
            TestStatement(
                "let a = 0; if (a > 0) a = 1; else a = 2;",
                expected:
                    @"let a = 0;
    if (a > 0) 
        a = 1;
    else 
        a = 2;");
        }

        [Fact]
        public void IfThenBlockElseNone()
        {
            TestStatement(
                "if (a > 0){break;}",
                expected:
                    @"    if (a > 0) {
        break;
    }");
        }

        [Fact]
        public void IfThenBlockElseBlock()
        {
            TestStatement(
                "if (a > 0){break;}else{break;}",
                expected:
                    @"    if (a > 0) {
        break;
    }
    else {
        break;
    }");
        }

        [Fact]
        public void Break()
        {
            TestStatement("break;", expected: @"    break;");
        }

        [Fact]
        public void Return()
        {
            TestStatement(
                "return 1;",
                expected: @"    return 1;");
        }

        [Fact]
        public void NestedIf()
        {
            TestStatement(
                "if (a > 0)if (a > 0){break;}",
                expected:
                    @"    if (a > 0) 
        if (a > 0) {
            break;
        }");
        }

        [Fact]
        public void SwitchOneCaseAssignmentNoDefault()
        {
            TestStatement(
                "switch (1) {case 1: let a = 1;}",
                expected: @"    switch (1) {
        case 1:
            let a = 1;
    }");
        }

        [Fact]
        public void SwitchTwoCaseBlockNoDefault()
        {
            TestStatement(
                "switch (1) {case 1: let x = 1; break; case 2: let y = 2; break;}",
                expected: @"    switch (1) {
        case 1:
            let x = 1;
            break;
        case 2:
            let y = 2;
            break;
    }");
        }

        [Fact]
        public void SwitchOneCaseAssignmentWithDefaultAssignment()
        {
            TestStatement(
                "switch (1) { case 1: let a = 1; default: let b = 2; break; }",
                expected: @"    switch (1) {
        case 1:
            let a = 1;
        default:
            let b = 2;
            break;
    }");
        }

        [Fact]
        public void AssignmentStatement()
        {
            TestStatement(
                "let x : number = 3; x += 1;",
                expected:
                    @"    let x : number = 3;
    x += 1;");
        }

        [Fact]
        public void ForLoop()
        {
            TestStatement(
                "for (let x = 0; Number.Lt(x, 10); x += 1) { x = x + 1; }",
                expected:
                    @"    for (let x = 0; Number.Lt(x, 10); x += 1) {
        x = x + 1;
    }");
        }

        [Fact]
        public void ForOfLoop()
        {
            TestStatement(
                "for (let x of xs) { x = x + 1; }",
                expected:
                    @"    for (let x of xs) {
        x = x + 1;
    }");
        }

        private void TestStatement(string source, string expected = null)
        {
            expected = expected ?? "    " + source;

            source = "const a = () => {" + source + "};";
            expected = "const a = () => {" + Environment.NewLine + expected + Environment.NewLine + "};";

            PrettyPrint(source, expected);
        }
    }
}
