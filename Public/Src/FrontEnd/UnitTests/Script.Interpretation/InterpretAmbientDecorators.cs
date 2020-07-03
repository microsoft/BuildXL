// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    /// <summary>
    /// Set of tests for ambient decorators like
    /// <code>
    /// @@qualifier({name: 'foo'})
    /// import * as X from 'foo.dsc';
    /// </code>
    ///
    /// Please note, that currently ambient decorators doesn't have any runtime effects.
    /// </summary>
    public sealed class InterpretAmbientDecorators : DsTest
    {
        public InterpretAmbientDecorators(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void InterpretInterfaceWithAmbientDecorators()
        {
            string code = @"
@@toolOption({name: 'tool'})
interface CscArgs {
  @@toolOption({name: 'references'})
  references: string[];
}

const args: CscArgs = { references: ['1.dll'] };
export const r = args.references.length;
";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(1, result);
        }
    }
}
