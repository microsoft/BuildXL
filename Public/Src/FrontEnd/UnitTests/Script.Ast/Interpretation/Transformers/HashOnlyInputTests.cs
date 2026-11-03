// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Pips.Operations;
using Test.BuildXL.FrontEnd.Core;
using Test.DScript.Ast;
using Xunit;
using static Test.BuildXL.TestUtilities.TestEnv;

namespace Test.BuildXL.FrontEnd.Script.Interpretation.Transformers
{
    public class HashOnlyInputTests : DsTest
    {
        private readonly TestPipGraph m_pipGraph = new TestPipGraph();

        public HashOnlyInputTests(ITestOutputHelper output) : base(output) { }

        protected override TestPipGraph GetPipGraph() => m_pipGraph;

        [Theory]
        [InlineData("file", false, false)]
        [InlineData("file", false, true)]
        [InlineData("file", true, false)]
        [InlineData("file", true, true)]
        [InlineData("exclusive", false, false)]
        [InlineData("exclusive", false, true)]
        [InlineData("exclusive", true, false)]
        [InlineData("exclusive", true, true)]
        [InlineData("shared", false, false)]
        [InlineData("shared", false, true)]
        [InlineData("shared", true, false)]
        [InlineData("shared", true, true)]
        public void RegularInputTakesPrecedenceRegardlessOfDeclarationOrder(string kind, bool declareHashOnlyInputFirst, bool compound)
        {
            // Regular declarations win over hash-only declarations in either order, including within compound arguments.
            string declarations = kind == "file"
                ? "const input = f`input.txt`;"
                : $@"
const producer = Transformer.execute({{
    tool: {{exe: f`dummy.exe`}},
    workingDirectory: d`.`,
    arguments: [],
    outputs: [{{kind: ""{kind}"", directory: d`opaque`}}]
}});
const input = producer.getOutputDirectory(d`opaque`);";
            string first = declareHashOnlyInputFirst ? "Artifact.vsoHash(input)" : "Artifact.input(input)";
            string second = declareHashOnlyInputFirst ? "Artifact.input(input)" : "Artifact.vsoHash(input)";
            string arguments = compound
                ? $"Cmd.argument(Cmd.join(',', [{first}, {second}]))"
                : $"Cmd.argument({first}), Cmd.argument({second})";
            string spec = $@"
import {{Transformer, Artifact, Cmd}} from 'Sdk.Transformers';
{declarations}
export const result = Transformer.execute({{
    tool: {{exe: f`dummy.exe`}},
    workingDirectory: d`.`,
    arguments: [{arguments}]
}});";
            EvaluateExpressionWithNoErrors(spec, "result");
            var process = m_pipGraph.RetrieveScheduledPips().OfType<Process>()
                .Single(pip => pip.RegularDependencies.Any(file => file.Path.ToString(PathTable).EndsWith("input.txt"))
                    || pip.RegularDirectoryDependencies.Any());
            Assert.Empty(process.HashOnlyFileDependencies);
            Assert.Empty(process.HashOnlyDirectoryDependencies);
            if (kind == "file")
            {
                var input = Assert.Single(process.Dependencies.Where(file => file.Path.ToString(PathTable).EndsWith("input.txt")));
                Assert.Contains(input, process.RegularDependencies);
            }
            else
            {
                var input = Assert.Single(process.DirectoryDependencies);
                Assert.Contains(input, process.RegularDirectoryDependencies);
                Assert.Equal(kind == "shared", input.IsSharedOpaque);
            }
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public void HashArgumentsRegisterContentDependencies(bool directoryInput, bool isRegularInput, bool compound)
        {
            // File/directory hash arguments register hash-only dependencies unless an explicit regular input is also declared.
            string hashArgument = compound
                ? "Cmd.argument(Cmd.join(',', [Artifact.vsoHash(input), Artifact.vsoHash(input)]))"
                : "Cmd.argument(Artifact.vsoHash(input))";
            string declarations = directoryInput
                ? @"
const producer = Transformer.execute({
    tool: {exe: f`dummy.exe`},
    workingDirectory: d`.`,
    arguments: [],
    outputs: [{kind: ""shared"", directory: d`opaque`}]
});
const input = producer.getOutputDirectory(d`opaque`);"
                : "const input = f`input.txt`;";
            string spec = $@"
import {{Transformer, Artifact, Cmd}} from 'Sdk.Transformers';
{declarations}
export const result = Transformer.execute({{
    tool: {{exe: f`dummy.exe`}},
    workingDirectory: d`.`,
    arguments: [{hashArgument}],
    dependencies: {(isRegularInput ? "[input]" : "[]")}
}});";
            EvaluateExpressionWithNoErrors(spec, "result");
            var process = m_pipGraph.RetrieveScheduledPips().OfType<Process>()
                .Single(pip => directoryInput
                    ? pip.DirectoryDependencies.Any()
                    : pip.Dependencies.Any(file => file.Path.ToString(PathTable).EndsWith("input.txt")));
            if (directoryInput)
            {
                var input = Assert.Single(process.DirectoryDependencies);
                Assert.Equal(isRegularInput, process.RegularDirectoryDependencies.Contains(input));
                Assert.Equal(!isRegularInput, process.HashOnlyDirectoryDependencies.Contains(input));
            }
            else
            {
                var input = process.Dependencies.Single(file => file.Path.ToString(PathTable).EndsWith("input.txt"));
                Assert.Equal(isRegularInput, process.RegularDependencies.Contains(input));
                Assert.Equal(!isRegularInput, process.HashOnlyFileDependencies.Contains(input));
            }
        }
    }
}
