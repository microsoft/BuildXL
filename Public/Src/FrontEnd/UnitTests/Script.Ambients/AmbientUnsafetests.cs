// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;


namespace Test.DScript.Ast.Interpretation
{
    /// <summary>
    /// Tests for ambient unsafe.
    /// </summary>
    public sealed class AmbientUnsafeTests : DsTest
    {
        public AmbientUnsafeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestUnsafeOutputFile()
        {
            var spec = @"
export const x = Unsafe.outputFile(p`a/b/c`);
export const y = Unsafe.outputFile(p`d/e/f`, 2);
export const xPath = p`a/b/c`;
export const yPath = p`d/e/f`;
";
            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionsWithNoErrors("x", "y", "xPath", "yPath");

            var x = result.Get<FileArtifact>("x");
            var y = result.Get<FileArtifact>("y");
            var xPath = result.Get<AbsolutePath>("xPath");
            var yPath = result.Get<AbsolutePath>("yPath");

            XAssert.IsTrue(x.IsOutputFile);
            XAssert.AreEqual(xPath, x.Path);
            XAssert.IsTrue(y.IsOutputFile);
            XAssert.AreEqual(2, y.RewriteCount);
            XAssert.AreEqual(yPath, y.Path);
        }

        [Fact]
        public void TestUnsafeOutputDirectory()
        {
            var spec = @"
export const x = Unsafe.exOutputDirectory(p`a/b/c`);
export const xPath = p`a/b/c`;
";
            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionsWithNoErrors("x", "xPath");

            var x = result.Get<DirectoryArtifact>("x");
            var xPath = result.Get<AbsolutePath>("xPath");

            XAssert.AreEqual((uint)0, x.PartialSealId);
            XAssert.AreEqual(xPath, x.Path);            
        }
    }
}
