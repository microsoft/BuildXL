// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast;
using Xunit;
using static Test.BuildXL.TestUtilities.TestEnv;

namespace Test.BuildXL.FrontEnd.Script.Interpretation.Transformers
{
    public class AllowedUndeclaredSourceReadTests : DsTest
    {
        private readonly TestPipGraph m_pipGraph = new TestPipGraph();

        public AllowedUndeclaredSourceReadTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override TestPipGraph GetPipGraph() => m_pipGraph;

        [Fact]
        public void AllowedUndeclaredSourceReadsAreSet()
        {
            string spec = @"
import {Transformer} from 'Sdk.Transformers';

export const result = Transformer.execute({
    tool: { exe: f`dummy.exe` },
    arguments: [],
    workingDirectory: d`.`,
    allowUndeclaredSourceReads: true,
    allowedUndeclaredSourceReadScopes: [d`.`.path],
    allowedUndeclaredSourceReadPaths: [f`config.dsc`.path],
    allowedUndeclaredSourceReadRegexes: ['.*allowed-regex.*'],
});
";

            EvaluateExpressionWithNoErrors(spec, "result");

            var process = m_pipGraph.RetrieveScheduledPips().OfType<Process>().Single();
            XAssert.IsTrue((process.ProcessOptions & Process.Options.AllowUndeclaredSourceReads) != 0);
            XAssert.AreEqual(1, process.AllowedUndeclaredSourceReadScopes.Length);
            XAssert.IsTrue(process.AllowedUndeclaredSourceReadPaths.Any(path => path.ToString(PathTable).EndsWith("config.dsc")));
            var regex = process.AllowedUndeclaredSourceReadRegexes.Single();
            XAssert.AreEqual(".*allowed-regex.*", regex.Pattern.ToString(StringTable));

            var expectedOptions = OperatingSystemHelper.IsWindowsOS ? RegexOptions.IgnoreCase : RegexOptions.None;
#if NET7_0_OR_GREATER
            expectedOptions |= RegexOptions.NonBacktracking;
#endif
            XAssert.AreEqual(expectedOptions, regex.Options);
        }
    }
}
