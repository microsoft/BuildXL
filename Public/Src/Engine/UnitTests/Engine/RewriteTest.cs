// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
                ? (start: "-c \"", cmd: "/bin/cat ", end: " | /bin/cat \"")
                : (start: "/d /c ", cmd: "type ", end: " ");

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
        Cmd.rawArgument('{shellCmd.start}'),
        Cmd.rawArgument('{shellCmd.cmd}'),
        Cmd.argument(Artifact.none(step2OutputPath)),
        Cmd.rawArgument('{shellCmd.end}'),
        Cmd.rawArgument(' >> '),
        Cmd.argument(Artifact.rewritten(step1, step2OutputPath)),
    ],
}}).getOutputFile(step2OutputPath);

const step3OutputPath = p`obj/c.txt`;
const step3 = execute({{
    tool: cmd,
    workingDirectory: d`.`,
    arguments: [
        Cmd.rawArgument('{shellCmd.start}'),
        Cmd.rawArgument('{shellCmd.cmd}'),
        Cmd.argument(Artifact.none(step3OutputPath)),
        Cmd.rawArgument('{shellCmd.end}'),
        Cmd.rawArgument(' >> '),
        Cmd.argument(Artifact.rewritten(step2, step3OutputPath)),
    ],
}}).getOutputFile(step3OutputPath);

const step4OutputPath = p`obj/d.txt`;
const step4 = execute({{
    tool: cmd,
    workingDirectory: d`.`,
    arguments: [
        Cmd.rawArgument('{shellCmd.start}'),
        Cmd.rawArgument('{shellCmd.cmd}'),
        Cmd.argument(Artifact.none(step4OutputPath)),
        Cmd.rawArgument('{shellCmd.end}'),
        Cmd.rawArgument(' >> '),
        Cmd.argument(Artifact.rewritten(step3, step4OutputPath)),
    ],
}}).getOutputFile(step4OutputPath);
";
            AddModule("test", ("spec.dsc", spec), placeInRoot: true);
            RunEngine();

            string testFile = Path.Combine(Configuration.Layout.ObjectDirectory.ToString(Context.PathTable), "d.txt");
            XAssert.IsTrue(File.Exists(testFile));
            XAssert.AreEqual(@"AAAAAAAA", string.Join(string.Empty, File.ReadAllLines(testFile)));
        }
    }
}
