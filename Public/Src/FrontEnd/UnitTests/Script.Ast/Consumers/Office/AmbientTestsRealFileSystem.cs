// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Consumers.Office
{
    /// <summary>
    /// Tests for API used by Office.
    /// </summary>
    /// <remarks>
    /// Any change will break Office.
    /// </remarks>
    [Trait("Category", "Office")]
    public sealed class AmbientTestsRealFileSystem : DsTest
    {
        public AmbientTestsRealFileSystem(ITestOutputHelper output)
            : base(output, usePassThroughFileSystem: true)
        {
        }
        
        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientFileUses()
        {
            FileSystem = new PassThroughMutableFileSystem(PathTable);
            RelativeSourceRoot = System.IO.Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());

            var filePath = X("/D/path/to/a/file.txt");

            string spec = $@"
// Any change will break Office.
const file = f`{filePath}`;
const filePath = file.path;
const fileContent = File.readAllText(f`file.txt`);
";
            var results = Build()
                .Spec(spec)
                .AddFile("file.txt", "Hello")
                .EvaluateExpressionsWithNoErrors("file", "filePath", "fileContent");

            Assert.IsType<FileArtifact>(results["file"]);
            Assert.True(((FileArtifact) results["file"]).IsSourceFile);
            Assert.Equal(CreateAbsolutePath(filePath), results["filePath"]);
            Assert.Equal("Hello", results["fileContent"]);
        }

        private AbsolutePath CreateAbsolutePath(string path)
        {
            return AbsolutePath.Create(PathTable, path);
        }
    }
}
