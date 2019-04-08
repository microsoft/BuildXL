// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast
{
    [Trait("Category", "Parsing")]
    public sealed class TestUniqueOutputDirectoryForValues : DsTest
    {
        /// <nodoc />
        public TestUniqueOutputDirectoryForValues(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void TestModuleLiteralGetOutputFolderHint()
        {
            var parseResults = EvaluateExpressionsWithNoErrors(@"
    export const dLL = Context.getNewOutputDirectory('xxx');
    export const dll = Context.getNewOutputDirectory('xxx');
    export const exe = Context.getNewOutputDirectory('xxx');
", "dLL", "dll", "exe");

            var dllDir = (DirectoryArtifact)parseResults["dll"];
            var dLLDir = (DirectoryArtifact)parseResults["dLL"];
            var exeDir = (DirectoryArtifact)parseResults["exe"];

            Assert.True(dllDir.Path != dLLDir.Path);
            Assert.True(dllDir.Path != exeDir.Path);


            var dllString = dllDir.Path.ToString(PathTable);
            var dLLString = dLLDir.Path.ToString(PathTable);
            var exeString = exeDir.Path.ToString(PathTable);

            Assert.True(!string.Equals(dllString, dLLString, StringComparison.OrdinalIgnoreCase));
            Assert.True(!string.Equals(dllString, exeString, StringComparison.OrdinalIgnoreCase));

            var dllName = dllDir.Path.GetName(PathTable);
            var dLLName = dLLDir.Path.GetName(PathTable);
            var exeName = exeDir.Path.GetName(PathTable);

            Assert.True(dllName == dLLName);
            Assert.True(dllName == exeName);
        }
    }
}
