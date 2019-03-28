// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretObsoleteStuff : DsTest
    {
        public InterpretObsoleteStuff(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void CallObsoleteFunctionShouldFailWithWarning()
        {
            string code = @"
@@obsolete()
function foo() { return 42; }

export const r = foo();
";
            
            var diagnostic = EvaluateWithDiagnosticId(code, LogEventId.MemberIsObsolete);
            Output.WriteLine(diagnostic.FullMessage);

            Assert.Contains("Member 'foo' is obsolete", diagnostic.FullMessage);

            // Checking location as well.
            Assert.Contains("(5,18):", diagnostic.FullMessage);
        }

        [Fact]
        public void CallObsoleteFunctionInALoopShouldFailWithSingleWarning()
        {
            string code = @"
@@obsolete()
function foo() { return 42; }

function loop() {
    let x = 0;
    for (let i = 0; i < 3; i += 1) {
        x = x + foo();
    }

    return x;
}

export const r = loop();
";
            var diagnostic = EvaluateWithAllDiagnosticId(code, LogEventId.MemberIsObsolete, "r");

            foreach (var d in diagnostic)
            {
                Assert.Contains("Member 'foo' is obsolete", d.FullMessage);

                // Checking location as well.
                Assert.Contains("(8,17):", d.FullMessage);

                Output.WriteLine(d.FullMessage);
            }

            Assert.Equal(1, diagnostic.Count);
        }

        [Fact]
        public void UsingObsoleteEnumMemberShouldNotLeadToWarning()
        {
            string code = @"
export const enum Foo {
  @@obsolete()
  bar = 42
}

export const r = Foo.bar;
";

            // Unfortunately with current evaluation model obsolete feature does not support enums.
            EvaluateExpressionWithNoErrors(code, "r");
        }

        [Fact]
        public void CheckWarningLocationForObjectLiteral()
        {
            string code = @"
@@obsolete()
function foo() { return 42; }

export const r = {foo: foo()};
";
            var diagnostic = EvaluateWithDiagnosticId(code, LogEventId.MemberIsObsolete, "r");
            Output.WriteLine(diagnostic.FullMessage);

            Assert.Contains("Member 'foo' is obsolete", diagnostic.FullMessage);

            // Checking location as well.
            Assert.Contains("(5,24):", diagnostic.FullMessage);
        }

        [Fact]
        public void CallingObsoleteFunctionInALoopLeadToMultipleDiagnostics()
        {
            // Even that computation for obsolete members is happening at runtime there is just one
            // warning if an obsolete feature is called in the loop
            string code = @"
@@obsolete()
function foo(n: number) { return 42; }

function bar() {
  return [1, 2, 3].map(x => foo(x));
}
export const r = bar();
";
            var diagnostic = EvaluateWithAllDiagnosticId(code, LogEventId.MemberIsObsolete, "r");
            foreach (var d in diagnostic)
            {
                Output.WriteLine(d.FullMessage);
            }

            Assert.Equal(1, diagnostic.Count);
        }

        [Fact]
        public void CallingObsoleteFunctionInALoopLeadToSingleDiagnosticForInterfaceMember()
        {
            // Even that computation for obsolete members is happening at runtime there is just one
            // warning if an obsolete feature is called in the loop
            string code = @"
function bar() {
    return [p`a/b`, p`b/c`, p`d/e`].map((x : Path) => x.extend(''));
  }
export const r = bar();
";
            var diagnostic = EvaluateWithAllDiagnosticId(code, LogEventId.MemberIsObsolete, "r");

            foreach (var d in diagnostic)
            {
                Output.WriteLine(d.FullMessage);
            }

            Assert.Equal(1, diagnostic.Count);
        }

        [Fact]
        public void ExtendMethodIsObsolete()
        {
            string code = @"
const pp = p`foo.txt`;
export const r = pp.extend('foo');
";
            var diagnostic = EvaluateWithDiagnosticId(code, LogEventId.MemberIsObsolete, "r");
            Output.WriteLine(diagnostic.FullMessage);
            Assert.Contains("Member 'extend' is obsolete.", diagnostic.FullMessage);

            // Checking location as well.
            Assert.Contains("(3,18):", diagnostic.FullMessage);
        }

        [Fact]
        public void ObsoleteForInterfaceMember()
        {
            string code = @"
interface Foo {
  @@obsolete('my custom message')
  x: number;
}
export const f:Foo = {x: 42};
export const r = f.x;
";
            var diagnostic = EvaluateWithDiagnosticId(code, LogEventId.MemberIsObsolete, "r");
            Output.WriteLine(diagnostic.FullMessage);
            Assert.Contains("Member 'x' is obsolete. my custom message.", diagnostic.FullMessage);

            // Checking location as well.
            Assert.Contains("(7,18):", diagnostic.FullMessage);
        }
    }
}
