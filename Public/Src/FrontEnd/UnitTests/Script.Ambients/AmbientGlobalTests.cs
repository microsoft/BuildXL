// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientGlobalTests : DsTest
    {
        public AmbientGlobalTests(ITestOutputHelper output)
            : base(
                  output, 
                  usePassThroughFileSystem: true /* Need PassThroughFileSystem to test globbing directory with long path; see TestGlobingLongPath. */)
        {
        }

        [Fact]
        public void TestImportFrom()
        {
            var testWriter = CreateTestWriter(@"ImportNameTest/MyProject");
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"project.dsc", @"
namespace MyProject
{
    export const y0 = importFrom(""MyLibB"").x;
}");

            testWriter.AddExtraFile(@"../AuxLib/MyLibB/package.dsc", @"
export const x = 41 + 1;");

            testWriter.AddExtraFile(@"../AuxLib/MyLibB/package.config.dsc", @"
module({ name: ""MyLibB"" });");

            SourceResolverTestObject resolver = configWriter.AddSourceResolver();
            resolver.AddPackage("../AuxLib/MyLibB");

            configWriter.AddDefaultSourceResolver();

            var result = Evaluate(testWriter, @"project.dsc", new[] {"MyProject.y0"});

            result.ExpectNoError();
            result.ExpectValues(count: 1);

            Assert.Equal(42, result.Values[0]);
        }

        [Fact]
        public void TestUnresolvedImportFrom()
        {
            var testWriter = CreateTestWriter(@"ImportNameTest/MyProject");
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"project.dsc", @"
namespace MyProject
{
    export const y0 = importFrom(""MyLibC"").x;
}");

            testWriter.AddExtraFile(@"../AuxLib/MyLibB/package.dsc", @"
export const x = 41 + 1;");

            testWriter.AddExtraFile(@"../AuxLib/MyLibB/package.config.dsc", @"
module({ name: ""MyLibB"" });");

            SourceResolverTestObject resolver = configWriter.AddSourceResolver();
            resolver.AddPackage("../AuxLib/MyLibB");

            configWriter.AddDefaultSourceResolver();

            var result = Evaluate(testWriter, @"project.dsc", new[] {"MyProject.y0"});

            result.ExpectErrorCode((int)global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace, count: 1);
            result.ExpectErrorMessageSubstrings(new[] { "No resolver was found that owns module 'MyLibC'" });
        }

        [Fact]
        public void TestGlobNonExistentDirectoryShouldResultInEmptyArray()
        {
            const string Spec = @"const x = glob(d`nonexistent/directory`, '*.*');";
            var result = EvaluateExpressionWithNoErrors(Spec, "x");
            var array = result as ArrayLiteral;

            Assert.NotNull(array);
            Assert.Equal(0, array.Length);
        }

        [Fact]
        public void TestCachedGlob()
        {
            var testWriter = CreateTestWriter(@"CachedGlob");
            var configWriter = testWriter.ConfigWriter;

            const string Spec = @"
function myGlob(dummy, directory, pattern) {
    return glob(directory, pattern);
}

const x = myGlob(undefined, d`directory`, '*.*');
const y = myGlob(x, d`directory`, '*.*');
const z = myGlob(x, d`directory`, '*.txt');
const w = myGlob(x, d`directory2`, '*.*');
";
            configWriter.AddBuildSpec("spec.dsc", Spec);
            testWriter.AddExtraFile(@"directory/file.txt", string.Empty);
            testWriter.AddExtraFile(@"directory2/file.txt", string.Empty);

            var result = Evaluate(testWriter, "spec.dsc", new[] {"x", "y", "z", "w"});
            var arrayX = result.Values[0] as ArrayLiteral;
            var arrayY = result.Values[1] as ArrayLiteral;
            var arrayZ = result.Values[2] as ArrayLiteral;
            var arrayW = result.Values[3] as ArrayLiteral;

            Assert.True(ReferenceEquals(arrayX.Values, arrayY.Values));
            Assert.False(ReferenceEquals(arrayX.Values, arrayZ.Values));
            Assert.False(ReferenceEquals(arrayX.Values, arrayW.Values));
        }

        [Fact]
        public void TestGlobingLongPath()
        {
            var testWriter = CreateTestWriter(nameof(TestGlobingLongPath));
            var configWriter = testWriter.ConfigWriter;

            string testDir = Path.Combine(TestRoot, Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDir);
            var fileSystem = testWriter.FileSystem;
            var pathTable = testWriter.PathTable;

            // Create 1.first/first.txt
            string first = Path.Combine(testDir, "1.first");
            fileSystem.CreateDirectory(AbsolutePath.Create(pathTable, first));
            fileSystem.WriteAllText(Path.Combine(first, "first.txt"), "first");


            // Create 2.middle
            string middle = Path.Combine(testDir, "2.middle");
            fileSystem.CreateDirectory(AbsolutePath.Create(pathTable, middle));
            CreateDirectoryWithLongPath(middle, fileSystem);

            // Create 3.last/last.txt
            string last = Path.Combine(testDir, "3.last");
            fileSystem.CreateDirectory(AbsolutePath.Create(pathTable, last));
            fileSystem.WriteAllText(Path.Combine(last, "last.txt"), "last");


            string spec = "const x = globR(d`__PATH__`, \"*\");".Replace("__PATH__", testDir.Replace('\\', '/'));
            configWriter.AddBuildSpec("spec.dsc", spec);

            var result = Evaluate(testWriter, "spec.dsc", new[] { "x" });
            var arrayX = result.Values[0] as ArrayLiteral;
            Assert.Equal(2, arrayX.Count);
        }

        [Fact]
        public void AddIfCallbackFail()
        {
            var result = EvaluateSpec(@"
const result : number[] = addIfLazy<number>(true, () => { 
    Contract.fail(""contract: failure""); 
    return [1];
});
"
                , new[] { "result" });

            result.ExpectValues(count: 1);
            result.ExpectErrors(count: 1);
            result.ExpectErrorCode((int)LogEventId.ContractFail, count: 1);
            Assert.Equal(ErrorValue.Instance, result.Values[0]);

            result.ExpectErrorMessageSubstrings(
                new[]
                {
                    "contract: failure",
                });
        }


        [Theory]
        [InlineData("addIf(false)")]
        [InlineData("addIf(false, 1)")]
        [InlineData("addIf(true)")]
        [InlineData("addIf(true, 1)", 1)]

        [InlineData("addIfLazy(false, () => [])")]
        [InlineData("addIfLazy(false, () => [1])")]
        [InlineData("addIfLazy(true, () => [])")]
        [InlineData("addIfLazy(true, () => [1])", 1)]
        [InlineData("addIfLazy(false, () => { Contract.fail(\"contract: failure\"); return [1]; })")]

        [InlineData("[1,2,...addIf(false, 3, 4), 5]", 1, 2, 5)]
        [InlineData("[1,2,...addIf(true, 3, 4), 5]", 1, 2, 3, 4, 5)]
        [InlineData("[1,2,...addIfLazy(false, () => [3, 4]), 5]", 1, 2, 5)]
        [InlineData("[1,2,...addIfLazy(true, () => [3, 4]), 5]", 1, 2, 3, 4, 5)]
        public void AddIf(string expr, params int[] expectedValues)
        {
            var result = EvaluateSpec("const result = " + expr + ";", new[] { "result" });

            result.ExpectValues(count: 1);
            var resultValue = result.Values[0] as EvaluatedArrayLiteral;


            var actualValues = new int[resultValue.Count];
            for (int i = 0; i < actualValues.Length; i++)
            {
                var item = resultValue[i].Value;

                actualValues[i] = (int)item;
            }

            Assert.Equal(expectedValues, actualValues);
        }



        private void CreateDirectoryWithLongPath(string root, IMutableFileSystem fileSystem)
        {
            const int MaxPath = 260;

            // Create root/0001/.../00XX.
            int i = 0;
            string current = root;
            while (true)
            {
                current = Path.Combine(current, i.ToString("D4"));
                var path = AbsolutePath.Create(fileSystem.GetPathTable(), current);
                fileSystem.CreateDirectory(path);

                if (current.Length > MaxPath)
                {
                    return;
                }
            }
        }
    }
}
