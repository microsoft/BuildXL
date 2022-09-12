// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Consumers.Office
{
    [Trait("Category", "Office")]
    public sealed class CmdTests : DsTest
    {
        public CmdTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void CmdUses()
        {
            var rootPath = X("/D/xyz/abc");
            var outFile1 = X("/D/xyz/out.txt");
            var outFile2 = X("/D/xyz/file.txt");

            string spec = $@"
// Any change will break Office.
import {{Artifact, Cmd}} from 'Sdk.Transformers';
const arguments = [
    Cmd.startUsingResponseFileWithPrefix("""", true),
    Cmd.rawArgument(""foo bar""),
    Cmd.flag(""/nologo:"", true),
    Cmd.option(""/uid:"", ""1234""),
    Cmd.option(""/projects:"", Cmd.join("";"", [""orapi"", ""ppt""])),
    Cmd.option(""/rt:"", Artifact.none(d`{rootPath}`)),
    Cmd.option(""/out:"", Artifact.output(p`{outFile1}`)),
    Cmd.argument(Artifact.input(f`{outFile2}`))
];
const argumentsInString = Debug.dumpArgs(arguments);
";
            var results = Build()
                .Spec(spec)
                .AddFullPrelude()
                .EvaluateExpressionsWithNoErrors("argumentsInString");

            Assert.Equal(
                $@"foo bar /nologo: /uid:1234 /projects:orapi;ppt /rt:{rootPath} /out:{outFile1} {outFile2}".ToUpperInvariant(),
                ((string)results["argumentsInString"]).ToUpperInvariant());
        }
    }
}
