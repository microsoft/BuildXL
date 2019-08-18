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
";
            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionsWithNoErrors("x", "y");

            var x = result.Get<FileArtifact>("x");
            var y = result.Get<FileArtifact>("y");

            XAssert.IsTrue(x.IsOutputFile);
            XAssert.IsTrue(x.Path.ToString(PathTable).EndsWith(@"a\b\c", System.StringComparison.OrdinalIgnoreCase));
            XAssert.IsTrue(y.IsOutputFile);
            XAssert.AreEqual(2, y.RewriteCount);
            XAssert.IsTrue(y.Path.ToString(PathTable).EndsWith(@"d\e\f", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void TestUnsafeOutputDirectory()
        {
            var spec = @"
export const x = Unsafe.exOutputDirectory(p`a/b/c`);
";
            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionsWithNoErrors("x");

            var x = result.Get<DirectoryArtifact>("x");

            XAssert.AreEqual((uint)0, x.PartialSealId);
            XAssert.IsTrue(x.Path.ToString(PathTable).EndsWith(@"a\b\c", System.StringComparison.OrdinalIgnoreCase));            
        }
    }
}
