// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.FrontEnd.Core;
using Xunit;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretObsoleteStuff : DsTest
    {
        private bool? m_disableIsObsoleteCheckDuringConversion = false;

        public InterpretObsoleteStuff(ITestOutputHelper output) : base(output) { }

        protected override FrontEndConfiguration GetFrontEndConfiguration(bool isDebugged)
        {
            var configuration = base.GetFrontEndConfiguration(isDebugged);
            configuration.DisableIsObsoleteCheckDuringConversion = m_disableIsObsoleteCheckDuringConversion;
            return configuration;
        }

        [Theory]
        [InlineData(null, 0)]
        [InlineData(true, 0)]
        [InlineData(false, 2)]
        public void ObsoleteCheckingCanBeConfigured(bool? disableCheck, int expectedWarningCount)
        {
            m_disableIsObsoleteCheckDuringConversion = disableCheck;
            string code = @"
@@obsolete()
function foo() { return 42; }

export const r = foo() + foo();
";
            var result = EvaluateSpec(code, new[] { "r" });

            Assert.Empty(result.Errors);
            Assert.Equal(84, Assert.Single(result.Values));
            Assert.Equal(expectedWarningCount, result.Diagnostics.Count(diagnostic => diagnostic.ErrorCode == (int)LogEventId.MemberIsObsolete));
        }

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

        [Theory]
        [InlineData("@@obsolete()", "Member 'foo' is obsolete.")]
        [InlineData("@@obsolete('use replacement')", "Member 'foo' is obsolete. use replacement.")]
        public void RepeatedObsoleteFunctionWarnsAtDistinctUseSites(string obsoleteDecorator, string expectedMessage)
        {
            string code = $@"
{obsoleteDecorator}
function foo() {{ return 42; }}

export const first = foo();
export const other = foo();
export const r = [first, other];
";
            var diagnostics = EvaluateWithAllDiagnosticId(code, LogEventId.MemberIsObsolete, "r");

            foreach (var diagnostic in diagnostics)
            {
                Output.WriteLine(diagnostic.FullMessage);
            }

            Assert.Equal(2, diagnostics.Count);
            Assert.All(diagnostics, diagnostic => Assert.Contains(expectedMessage, diagnostic.FullMessage));
            Assert.Contains(diagnostics, diagnostic => diagnostic.FullMessage.Contains("(5,22):"));
            Assert.Contains(diagnostics, diagnostic => diagnostic.FullMessage.Contains("(6,22):"));
        }
    }
}
