// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void CmdUses()
        {
            const string Spec = @"
// Any change will break Office.
import {Artifact, Cmd} from 'Sdk.Transformers';
const arguments = [
    Cmd.startUsingResponseFileWithPrefix("""", true),
    Cmd.rawArgument(""foo bar""),
    Cmd.flag(""/nologo:"", true),
    Cmd.option(""/uid:"", ""1234""),
    Cmd.option(""/projects:"", Cmd.join("";"", [""orapi"", ""ppt""])),
    Cmd.option(""/rt:"", Artifact.none(d`D:/xyz/abc`)),
    Cmd.option(""/out:"", Artifact.output(p`D:/xyz/out.txt`)),
    Cmd.argument(Artifact.input(f`D:/xyz/file.txt`))
];
const argumentsInString = Debug.dumpArgs(arguments);
";
            var results = Build()
                .Spec(Spec)
                .AddFullPrelude()
                .EvaluateExpressionsWithNoErrors("argumentsInString");

            Assert.Equal(
                @"foo bar /nologo: /uid:1234 /projects:orapi;ppt /rt:D:\xyz\abc /out:D:\xyz\out.txt D:\xyz\file.txt".ToUpperInvariant(),
                ((string)results["argumentsInString"]).ToUpperInvariant());
        }
    }
}
