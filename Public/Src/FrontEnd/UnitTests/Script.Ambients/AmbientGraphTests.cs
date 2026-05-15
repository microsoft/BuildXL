// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using static Test.BuildXL.TestUtilities.TestEnv;

namespace Test.DScript.Ast.Interpretation.Ambients
{
    public class AmbientGraphTests : DsTest
    {
        public AmbientGraphTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override TestPipGraph GetPipGraph() => new TestPipGraph();

        [Fact]
        public void GetDirectDependenciesReturnsDependencies()
        {
            string code = @"
import {Transformer} from 'Sdk.Transformers';

const tool = { exe: f`dummy.exe` };
const out = Context.getNewOutputDirectory('d1');
const inputFile = f`input.txt`;
const dirContent = f`dirFile.txt`;
const sealedDir = Transformer.sealDirectory(d`myDir`, [dirContent]);

const pip = Transformer.execute({
    tool: tool,
    arguments: [],
    workingDirectory: out,
    dependencies: [inputFile, sealedDir],
});

export const result = _PreludeAmbientHack_Graph.getDirectDependencies(pip);
";
            var result = EvaluateExpressionWithNoErrors(code, "result");
            var array = Assert.IsAssignableFrom<ArrayLiteral>(result);

            // Collect the types of all returned elements
            var elements = Enumerable.Range(0, array.Length).Select(i => array[i].Value).ToList();

            // Should contain file artifacts (at least dummy.exe and input.txt)
            var fileArtifacts = elements.OfType<FileArtifact>().ToList();
            Assert.True(fileArtifacts.Count >= 2, $"Expected at least 2 file inputs (tool exe + input file), but got {fileArtifacts.Count}");

            // Verify the declared input file is present
            Assert.True(
                fileArtifacts.Any(f => f.Path.ToString(PathTable).EndsWith("input.txt")),
                "Expected input.txt to be in the returned file artifacts");

            // Verify the tool exe is present
            Assert.True(
                fileArtifacts.Any(f => f.Path.ToString(PathTable).EndsWith("dummy.exe")),
                "Expected dummy.exe to be in the returned file artifacts");

            // Should contain a static directory (the fully sealed directory)
            var staticDirs = elements.OfType<StaticDirectory>().ToList();
            Assert.True(staticDirs.Count >= 1, $"Expected at least 1 directory input (sealed dir), but got {staticDirs.Count}");

            // Verify the sealed directory contains the declared content (dirFile.txt)
            var sealedDir = staticDirs[0];
            Assert.Equal(SealDirectoryKind.Full, sealedDir.SealDirectoryKind);
            var sealedFiles = sealedDir.Contents.Select(f => f.Path.ToString(PathTable)).ToList();
            Assert.True(
                sealedFiles.Any(f => f.EndsWith("dirFile.txt")),
                "Expected dirFile.txt to be in the sealed directory contents");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GetDirectDependenciesCollectsAllInputTypes(bool excludeSources)
        {
            string optionsArg = excludeSources ? ", {excludeSources: true}" : "";
            string code = $@"
import {{Transformer}} from 'Sdk.Transformers';

const tool = {{ exe: f`dummy.exe` }};
const out = Context.getNewOutputDirectory('d1');

// A plain source file
const sourceFile = f`source.txt`;

// A file produced by a copy pip
const copiedFile = Transformer.copyFile(f`original.txt`, p`${{out}}/copied.txt`);

// A file produced by a write pip
const writtenFile = Transformer.writeFile(p`${{out}}/written.txt`, 'hello');

// A fully sealed directory with known content
const sealedDir = Transformer.sealDirectory(d`myDir`, [f`myDir/a.txt`, f`myDir/b.txt`]);

// An opaque output directory from another process
const producerPip = Transformer.execute({{
    tool: tool,
    arguments: [],
    workingDirectory: Context.getNewOutputDirectory('producer'),
    outputs: [{{directory: d`${{out}}/opaqueOut`, kind: 'shared'}}],
}});
const opaqueDir = producerPip.getOutputDirectory(d`${{out}}/opaqueOut`);

// The main process consumes all of the above
const pip = Transformer.execute({{
    tool: tool,
    arguments: [],
    workingDirectory: Context.getNewOutputDirectory('main'),
    dependencies: [sourceFile, copiedFile, writtenFile, sealedDir, opaqueDir],
}});

export const result = _PreludeAmbientHack_Graph.getDirectDependencies(pip{optionsArg});
";
            var result = EvaluateExpressionWithNoErrors(code, "result");
            var array = Assert.IsAssignableFrom<ArrayLiteral>(result);

            var elements = Enumerable.Range(0, array.Length).Select(i => array[i].Value).ToList();

            var fileArtifacts = elements.OfType<FileArtifact>().ToList();
            var filePaths = fileArtifacts.Select(f => f.Path.ToString(PathTable)).ToList();

            var staticDirs = elements.OfType<StaticDirectory>().ToList();

            // Output files should always be present
            Assert.True(filePaths.Any(f => f.EndsWith("copied.txt")), "Expected copied.txt (output file) in inputs");
            Assert.True(filePaths.Any(f => f.EndsWith("written.txt")), "Expected written.txt (output file) in inputs");

            // Shared opaque directory (output) should always be present
            var opaqueSeal = staticDirs.FirstOrDefault(d => d.SealDirectoryKind == SealDirectoryKind.SharedOpaque);
            Assert.NotNull(opaqueSeal);

            if (excludeSources)
            {
                // Source files should be excluded
                Assert.False(filePaths.Any(f => f.EndsWith("source.txt")), "source.txt (source file) should be excluded");
                Assert.False(filePaths.Any(f => f.EndsWith("dummy.exe")), "dummy.exe (source file) should be excluded");

                // Full sealed directory (contains sources) should be excluded
                var fullSeal = staticDirs.FirstOrDefault(d => d.SealDirectoryKind == SealDirectoryKind.Full);
                Assert.Null(fullSeal);
            }
            else
            {
                // Source files should be present
                Assert.True(filePaths.Any(f => f.EndsWith("source.txt")), "Expected source.txt in file inputs");
                Assert.True(filePaths.Any(f => f.EndsWith("dummy.exe")), "Expected dummy.exe in file inputs");

                // Fully sealed directory should be present with content
                var fullSeal = staticDirs.FirstOrDefault(d => d.SealDirectoryKind == SealDirectoryKind.Full);
                Assert.NotNull(fullSeal);
                var sealedFiles = fullSeal.Contents.Select(f => f.Path.ToString(PathTable)).ToList();
                Assert.True(sealedFiles.Any(f => f.EndsWith("a.txt")), "Expected a.txt in sealed directory contents");
                Assert.True(sealedFiles.Any(f => f.EndsWith("b.txt")), "Expected b.txt in sealed directory contents");
            }
        }

        [Fact]
        public void GetDependencyClosureComputesTransitiveClosure()
        {
            // Build a diamond dependency graph:
            //
            //        pipD
            //       /    \
            //    pipB    pipC
            //       \    /
            //        pipA
            //
            // pipA produces fileA
            // pipB consumes fileA, produces fileB
            // pipC consumes fileA, produces fileC
            // pipD consumes fileB and fileC
            //
            // getInputClosure(pipD) should return inputs from all four pips,
            // with fileA deduplicated (consumed by both pipB and pipC).
            string code = @"
import {Transformer} from 'Sdk.Transformers';

const tool = { exe: f`dummy.exe` };

const pipA = Transformer.execute({
    tool: tool,
    arguments: [],
    workingDirectory: Context.getNewOutputDirectory('a'),
    dependencies: [f`rootInput.txt`],
    outputs: [p`out/fileA.txt`],
});

const pipB = Transformer.execute({
    tool: tool,
    arguments: [],
    workingDirectory: Context.getNewOutputDirectory('b'),
    dependencies: [f`uniqueToB.txt`, pipA.getOutputFile(p`out/fileA.txt`)],
    outputs: [p`out/fileB.txt`],
});

const pipC = Transformer.execute({
    tool: tool,
    arguments: [],
    workingDirectory: Context.getNewOutputDirectory('c'),
    dependencies: [f`uniqueToC.txt`, pipA.getOutputFile(p`out/fileA.txt`)],
    outputs: [p`out/fileC.txt`],
});

const pipD = Transformer.execute({
    tool: tool,
    arguments: [],
    workingDirectory: Context.getNewOutputDirectory('d'),
    dependencies: [f`uniqueToD.txt`, pipB.getOutputFile(p`out/fileB.txt`), pipC.getOutputFile(p`out/fileC.txt`)],
    outputs: [p`out/fileD.txt`],
});

export const result = _PreludeAmbientHack_Graph.getDependencyClosure(pipD);
";
            var result = EvaluateExpressionWithNoErrors(code, "result");
            var array = Assert.IsAssignableFrom<ArrayLiteral>(result);

            var elements = Enumerable.Range(0, array.Length).Select(i => array[i].Value).ToList();
            var fileArtifacts = elements.OfType<FileArtifact>().ToList();
            var filePaths = fileArtifacts.Select(f => f.Path.ToString(PathTable)).ToList();

            // Direct inputs of pipD
            Assert.True(filePaths.Any(f => f.EndsWith("uniqueToD.txt")), "Expected uniqueToD.txt (direct input of pipD)");
            Assert.True(filePaths.Any(f => f.EndsWith("fileB.txt")), "Expected fileB.txt (output of pipB, input of pipD)");
            Assert.True(filePaths.Any(f => f.EndsWith("fileC.txt")), "Expected fileC.txt (output of pipC, input of pipD)");

            // Inputs from pipB and pipC (one hop)
            Assert.True(filePaths.Any(f => f.EndsWith("uniqueToB.txt")), "Expected uniqueToB.txt (input of pipB)");
            Assert.True(filePaths.Any(f => f.EndsWith("uniqueToC.txt")), "Expected uniqueToC.txt (input of pipC)");
            Assert.True(filePaths.Any(f => f.EndsWith("fileA.txt")), "Expected fileA.txt (output of pipA, input of pipB and pipC)");

            // Inputs from pipA (two hops)
            Assert.True(filePaths.Any(f => f.EndsWith("rootInput.txt")), "Expected rootInput.txt (input of pipA, transitive dep)");

            // Verify deduplication: fileA.txt should appear exactly once even though both pipB and pipC consume it
            Assert.Equal(1, filePaths.Count(f => f.EndsWith("fileA.txt")));

            // dummy.exe is used by all 4 pips but should appear only once
            Assert.Equal(1, filePaths.Count(f => f.EndsWith("dummy.exe")));

            // pipD's output should NOT be in the result (getInputClosure returns inputs, not outputs)
            Assert.False(filePaths.Any(f => f.EndsWith("fileD.txt")), "pipD's output fileD.txt should not be in the input closure");
        }

        [Theory]
        // Random object literal
        [InlineData("{}", "getDirectDependencies")]
        [InlineData("{}", "getDependencyClosure")]
        // Seal directory result (not a process pip)
        [InlineData("Transformer.sealDirectory(d`sealDir`, [f`sealContent.txt`])", "getDirectDependencies")]
        [InlineData("Transformer.sealDirectory(d`sealDir`, [f`sealContent.txt`])", "getDependencyClosure")]
        public void GetDirectDependenciesWithNonProcessPipProducesError(string expression, string function)
        {
            string code = $@"
import {{Transformer}} from 'Sdk.Transformers';

export const result = _PreludeAmbientHack_Graph.{function}({expression});
";
            var result = EvaluateWithFirstError(code);
            Assert.NotEqual(0, result.ErrorCode);
        }
    }
}
