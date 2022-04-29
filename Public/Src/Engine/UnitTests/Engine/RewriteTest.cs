// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    public sealed class RewriteTests : BaseEngineTest
    {
        public RewriteTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void Rewrite()
        {
            var shellCmd = OperatingSystemHelper.IsUnixOS
                ? (shellStart: "-c \"", cmdStart: "x=$(/bin/cat", cmdEnd: "); printf '%s\\\\n' $x ", shellEnd: "\"")
                : (shellStart: "/d /c ", cmdStart: "type ", cmdEnd: " ", shellEnd: " ");

            string spec = $@"
import {{Artifact, Cmd, Tool, Transformer}} from 'Sdk.Transformers';

{GetExecuteFunction()}

const cmd = {GetOsShellCmdToolDefinition()};
const step1 = Transformer.writeAllLines(
    p`obj/a.txt`,
    [ 'A' ]);

const step2OutputPath = p`obj/b.txt`;
const step2 = execute({{
    tool: cmd,
    workingDirectory: d`.`,
    arguments: [
        Cmd.rawArgument('{Escape(shellCmd.shellStart)}'),
        Cmd.rawArgument('{Escape(shellCmd.cmdStart)}'),
        Cmd.argument(Artifact.none(step2OutputPath)),
        Cmd.rawArgument('{Escape(shellCmd.cmdEnd)}'),
        Cmd.rawArgument(' >> '),
        Cmd.argument(Artifact.rewritten(step1, step2OutputPath)),
        Cmd.rawArgument('{Escape(shellCmd.shellEnd)}'),
    ],
}}).getOutputFile(step2OutputPath);

const step3OutputPath = p`obj/c.txt`;
const step3 = execute({{
    tool: cmd,
    workingDirectory: d`.`,
    arguments: [
        Cmd.rawArgument('{Escape(shellCmd.shellStart)}'),
        Cmd.rawArgument('{Escape(shellCmd.cmdStart)}'),
        Cmd.argument(Artifact.none(step3OutputPath)),
        Cmd.rawArgument('{Escape(shellCmd.cmdEnd)}'),
        Cmd.rawArgument(' >> '),
        Cmd.argument(Artifact.rewritten(step2, step3OutputPath)),
        Cmd.rawArgument('{Escape(shellCmd.shellEnd)}'),
    ],
}}).getOutputFile(step3OutputPath);

const step4OutputPath = p`obj/d.txt`;
const step4 = execute({{
    tool: cmd,
    workingDirectory: d`.`,
    arguments: [
        Cmd.rawArgument('{Escape(shellCmd.shellStart)}'),
        Cmd.rawArgument('{Escape(shellCmd.cmdStart)}'),
        Cmd.argument(Artifact.none(step4OutputPath)),
        Cmd.rawArgument('{Escape(shellCmd.cmdEnd)}'),
        Cmd.rawArgument(' >> '),
        Cmd.argument(Artifact.rewritten(step3, step4OutputPath)),
        Cmd.rawArgument('{Escape(shellCmd.shellEnd)}'),
    ],
}}).getOutputFile(step4OutputPath);
";
            AddModule("test", ("spec.dsc", spec), placeInRoot: true);
            RunEngine();

            string testFile = Path.Combine(Configuration.Layout.ObjectDirectory.ToString(Context.PathTable), "d.txt");
            XAssert.IsTrue(File.Exists(testFile));
            XAssert.AreEqual(@"AAAAAAAA", string.Join(string.Empty, File.ReadAllLines(testFile)));
        }

        static string Escape(string str) => str.Replace("'", "\\'");
    }
}
