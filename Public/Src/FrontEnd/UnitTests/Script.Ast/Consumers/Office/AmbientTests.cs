// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace Test.DScript.Ast.Consumers.Office
{
    /// <summary>
    /// Tests for API used by Office.
    /// </summary>
    /// <remarks>
    /// Any change will break Office.
    /// </remarks>
    [Trait("Category", "Office")]
    public sealed class AmbientTests : DsTest
    {
        public AmbientTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientArrayUses()
        {
            const string Spec = @"
// Any change will break Office.
const data = [1, 2, 3, 4, 5];
const testLength = data.length;
const testConcat = data.concat([6, 7, 8, 9]);
const testMap = data.map(d => d + 1);
const testMapDefined = data.mapDefined(d => d < 4 ? d : undefined);
const testMapMany = data.mapMany(d => [d, d]);
const testReduce = data.reduce((acc, d) => acc + d, 0);
const testFilter = data.filter(d => d >= 4);
const testUnique = testMapMany.unique();
";
            var results = EvaluateExpressionsWithNoErrors(
                Spec,
                "testLength",
                "testConcat",
                "testMap",
                "testMapDefined",
                "testMapMany",
                "testReduce",
                "testFilter",
                "testUnique");

            Assert.Equal(5, results["testLength"]);
            CheckArray<int>(CreateArrayLiteral(new object[] {1, 2, 3, 4, 5, 6, 7, 8, 9}), results["testConcat"]);
            CheckArray<int>(CreateArrayLiteral(new object[] {2, 3, 4, 5, 6}), results["testMap"]);
            CheckArray<int>(CreateArrayLiteral(new object[] {1, 2, 3}), results["testMapDefined"]);
            CheckArray<int>(CreateArrayLiteral(new object[] {1, 1, 2, 2, 3, 3, 4, 4, 5, 5}), results["testMapMany"]);
            Assert.Equal(15, results["testReduce"]);
            CheckArray<int>(CreateArrayLiteral(new object[] {4, 5}), results["testFilter"]);
            CheckArray<int>(CreateArrayLiteral(new object[] {1, 2, 3, 4, 5}), results["testUnique"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientContextUses()
        {
            const string Config = @"
config({
     mounts: [
        {
            name: a`NuGetCache`,
            path: p`D:/NuGetCache`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true
        },
    ],
});";
            const string Spec = @"
// Any change will break Office.
const outDir = Context.getNewOutputDirectory(""testOut"");
const tempDir = Context.getTempDirectory(""testTemp"");
const hasMount = Context.hasMount(""NuGetCache"");
const mountPath = Context.getMount(""NuGetCache"").path;
const name = Context.getLastActiveUseName();
const moduleName = Context.getLastActiveUseModuleName();
const userHomeDir = Context.getUserHomeDirectory();
";
                var results = Build()
                .LegacyConfiguration(Config)
                .Spec(Spec)
                .EvaluateExpressionsWithNoErrors(
                    "outDir",
                    "tempDir",
                    "hasMount",
                    "mountPath",
                    "name",
                    "moduleName",
                    "userHomeDir");

            Assert.IsType<DirectoryArtifact>(results["outDir"]);
            Assert.IsType<DirectoryArtifact>(results["tempDir"]);
            Assert.True((bool) results["hasMount"]);
            Assert.Equal(CreateAbsolutePath(@"D:\NuGetCache"), results["mountPath"]);
            Assert.Equal("name", (string)results["name"]);
            Assert.Equal("__Config__", (string)results["moduleName"]); // This test evaluates in the main config context for simplicity so that is why this returns a 'funny' module name.
            Assert.IsType<DirectoryArtifact>(results["userHomeDir"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientContractUses()
        {
            const string Spec = @"
// Any change will break Office.
function f(b: boolean): number {
    Contract.requires(b);
    return 0;
}

function g(): number {
    const x = f(true);
    Contract.assert(x === 0);
    return x + 1;
}

const x = g();
";
            var result = EvaluateExpressionWithNoErrors(Spec, "x");
            Assert.Equal(1, result);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientDebugUses()
        {
            const string Spec = @"
// Any change will break Office.
const x = Debug.writeLine(""Hello World!"");
";
            var result = EvaluateExpressionWithNoErrors(Spec, "x");
            Assert.Equal(UndefinedValue.Instance, result);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientPathQueryUses()
        {
            const string Spec = @"
// Any change will break Office.
const path = p`D:/path/to/a/file.txt`;
const extension = path.extension;
const hasExtension = path.hasExtension;
const parent = path.parent;
const hasParent = path.hasParent;
const name = path.name;
const nameWithoutExtension = path.nameWithoutExtension;
const isWithin = path.isWithin(p`D:/path/to`);
const relative = p`D:/path/to`.getRelative(path);
";
            var results = EvaluateExpressionsWithNoErrors(
                Spec,
                "extension",
                "hasExtension",
                "parent",
                "hasParent",
                "name",
                "nameWithoutExtension",
                "isWithin",
                "relative");

            Assert.Equal(CreatePathAtom(".txt"), results["extension"]);
            Assert.True((bool)results["hasExtension"]);
            Assert.Equal(CreateAbsolutePath(@"D:\path\to\a"), results["parent"]);
            Assert.True((bool)results["hasParent"]);
            Assert.Equal(CreatePathAtom("file.txt"), results["name"]);
            Assert.Equal(CreatePathAtom("file"), results["nameWithoutExtension"]);
            Assert.True((bool)results["isWithin"]);
            Assert.Equal(CreateRelativePath("a/file.txt"), results["relative"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientPathUses()
        {
            const string Spec = @"
// Any change will break Office.
const changeExtension = p`D:/path/to/a/file.in`.changeExtension("".out"");
const relocated = p`D:/a/b/foo.cs`.relocate(d`D:/a/b`, d`D:/c/d`);
";
            var results = EvaluateExpressionsWithNoErrors(Spec, "changeExtension", "relocated");
            Assert.Equal(CreateAbsolutePath(@"D:\path\to\a\file.out"), results["changeExtension"]);
            Assert.Equal(CreateAbsolutePath(@"D:\c\d\foo.cs"), results["relocated"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientFileUses()
        {
            const string Spec = @"
// Any change will break Office.
const file = f`D:/path/to/a/file.txt`;
const filePath = file.path;
const fileContent = File.readAllText(f`file.txt`);
";
            var results = Build()
                .Spec(Spec)
                .AddFile("file.txt", "Hello")
                .EvaluateExpressionsWithNoErrors("file", "filePath", "fileContent");

            Assert.IsType<FileArtifact>(results["file"]);
            Assert.True(((FileArtifact) results["file"]).IsSourceFile);
            Assert.Equal(CreateAbsolutePath(@"D:\path\to\a\file.txt"), results["filePath"]);
            Assert.Equal("Hello", results["fileContent"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientDirectoryUses()
        {
            const string Spec = @"
// Any change will break Office.
const dir = d`D:/path/to/a/directory`;
const dirPath = dir.path;
";
            var results = EvaluateExpressionsWithNoErrors(Spec, "dir", "dirPath");

            Assert.IsType<DirectoryArtifact>(results["dir"]);
            Assert.Equal(CreateAbsolutePath(@"D:\path\to\a\directory"), results["dirPath"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientPathAtomUses()
        {
            const string Spec = @"
// Any change will break Office.
const extension = a`file.txt`.extension;
const hasExtension = a`file.txt.in`.hasExtension;
const changeExtension = a`file.txt.in`.changeExtension("".out"");
";
            var results = EvaluateExpressionsWithNoErrors(Spec, "extension", "hasExtension", "changeExtension");

            Assert.Equal(CreatePathAtom(".txt"), results["extension"]);
            Assert.True((bool) results["hasExtension"]);
            Assert.Equal(CreatePathAtom("file.txt.out"), results["changeExtension"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientRelativePathUses()
        {
            const string Spec = @"
// Any change will break Office.
const relative = r`relative/path/to/file.txt.in`;
const extension = relative.extension;
const hasExtension = relative.hasExtension;
const changeExtension = relative.changeExtension("".out"");
const hasParent = relative.hasParent;
const parent = relative.parent;
const name = relative.name;
const nameWithoutExtension = relative.nameWithoutExtension;
";

            var results = EvaluateExpressionsWithNoErrors(
                Spec,
                "extension",
                "hasExtension",
                "changeExtension",
                "hasParent",
                "parent",
                "name",
                "nameWithoutExtension");

            Assert.Equal(CreatePathAtom(".in"), results["extension"]);
            Assert.True((bool) results["hasExtension"]);
            Assert.Equal(CreateRelativePath(@"relative\path\to\file.txt.out"), results["changeExtension"]);
            Assert.True((bool) results["hasParent"]);
            Assert.Equal(CreateRelativePath(@"relative\path\to"), results["parent"]);
            Assert.Equal(CreatePathAtom("file.txt.in"), results["name"]);
            Assert.Equal(CreatePathAtom("file.txt"), results["nameWithoutExtension"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientStaticDirectoryUses()
        {
            const string Spec = @"
import {Transformer} from 'Sdk.Transformers';

// Any change will break Office.
const sealedDir = Transformer.sealDirectory(d`D:/path/to/dir`, [ f`D:/path/to/dir/a.txt`, f`D:/path/to/dir/b.txt`, f`D:/path/to/dir/subdir/c.txt`]);
const dirPath = sealedDir.root.path;
const contents = sealedDir.contents;
";
            var results = EvaluateExpressionsWithNoErrors(Spec, "dirPath", "contents");
            Assert.Equal(CreateAbsolutePath(@"D:\path\to\dir"), results["dirPath"]);
            Assert.IsAssignableFrom<ArrayLiteral>(results["contents"]);
            var arrayContent = results["contents"] as ArrayLiteral;
            Assert.NotNull(arrayContent);
            Assert.Equal(3, arrayContent.Length);
            Assert.Equal(CreateSourceFile(@"D:\path\to\dir\a.txt"), arrayContent[0]);
            Assert.Equal(CreateSourceFile(@"D:\path\to\dir\b.txt"), arrayContent[1]);
            Assert.Equal(CreateSourceFile(@"D:\path\to\dir\subdir\c.txt"), arrayContent[2]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestPathInterpolationUses()
        {
            const string Spec = @"
// Any change will break Office.
const dir = d`D:/path/to/a/dir`;
const fileName = a`file.txt`;
const relative = r`subdir/subsubdir`;
const name = ""file"";
const relativeString = ""subdir/subsubdir"";
const iPath1 = p`${dir}/x/${relative}/y/${fileName}`;
const iPath2 = p`${dir}/x/${relativeString}/y/${name + "".txt""}`;
";
            var results = EvaluateExpressionsWithNoErrors(Spec, "iPath1", "iPath2");
            var path = CreateAbsolutePath(@"D:\path\to\a\dir\x\subdir\subsubdir\y\file.txt");

            Assert.Equal(path, results["iPath1"]);
            Assert.Equal(path, results["iPath2"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientGlobalUses()
        {
            const string Spec = @"
// Any change will break Office.
const testGlob = glob(d`dir`, ""*.*"");
const testGlobR = globR(d`dir`, ""*.*"");
const testGlobRecursively = globRecursively(d`dir`, ""*.*"");
const globResult = [f`dir/a.txt`, f`dir/b.txt`];
const globRResult = [...globResult, f`dir/subdir/c.txt`];
";
            var results = Build()
                .AddSpec("project.dsc", Spec)
                .RootSpec("project.dsc")
                .AddFile(@"dir\a.txt", "A")
                .AddFile(@"dir\b.txt", "B")
                .AddFile(@"dir\subdir\c.txt", "C")
                .EvaluateExpressionsWithNoErrors("testGlob", "testGlobR", "testGlobRecursively", "globResult", "globRResult");

            CheckUnorderedArray<FileArtifact>(results["globResult"], results["testGlob"]);
            CheckUnorderedArray<FileArtifact>(results["globRResult"], results["testGlobR"]);
            CheckUnorderedArray<FileArtifact>(results["globRResult"], results["testGlobRecursively"]);

        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientStringUses()
        {
            const string Spec = @"
// Any change will break Office.
const str = ""Hello World"";
const indexOf = str.indexOf(""Wo"");
const length = str.length;
const replace = str.replace(""Hello"", ""Yellow"");
const split = str.split("" "");
const lowerCase = str.toLowerCase();
const slice = str.slice(0, indexOf - 1);
const isUndefinedOrWhitespace = String.isUndefinedOrWhitespace(str);
const join = String.join("" "", split);
const interpolate = `Excuse me, ${slice}, BuildXL!`;
";
            var results = EvaluateExpressionsWithNoErrors(
                Spec,
                "indexOf",
                "length",
                "replace",
                "split",
                "lowerCase",
                "slice",
                "isUndefinedOrWhitespace",
                "join",
                "interpolate");

            Assert.Equal(6, results["indexOf"]);
            Assert.Equal(11, results["length"]);
            Assert.Equal("Yellow World", results["replace"]);
            Assert.IsAssignableFrom<ArrayLiteral>(results["split"]);
            var splitArray = results["split"] as ArrayLiteral;
            Assert.NotNull(splitArray);
            Assert.Equal("Hello", splitArray[0].Value);
            Assert.Equal("World", splitArray[1].Value);
            Assert.Equal("hello world", results["lowerCase"]);
            Assert.Equal("Hello", results["slice"]);
            Assert.False((bool) results["isUndefinedOrWhitespace"]);
            Assert.Equal("Hello World", results["join"]);
            Assert.Equal("Excuse me, Hello, BuildXL!", results["interpolate"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientObjectUses()
        {
            const string Spec = @"
// Any change will break Office.
const x = { a: 1, b: 2, d: 5 };
const y = <{a : number, b: number, c: number, d: number, e: number}>(x.override({b: 3, c: 4, d: undefined}));
const a = y.a;
const b = y.b;
const c = y.c;
const d = y.d;
const e = y.e;
";
            var results = EvaluateExpressionsWithNoErrors(Spec, "a", "b", "c", "d", "e");
            Assert.Equal(1, results["a"]);
            Assert.Equal(3, results["b"]);
            Assert.Equal(4, results["c"]);
            Assert.Equal(UndefinedValue.Instance, results["d"]);
            Assert.Equal(UndefinedValue.Instance, results["e"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientEnvironmentUses()
        {
            const string Spec = @"
// Any change will break Office.
const newLine = Environment.newLine();
const hasVariable = Environment.hasVariable(""ProgramFiles(x86)"");
const stringValue = Environment.getStringValue(""UserName"");
const pathValue = Environment.getPathValue(""ProgramFiles(x86)"");
const fileValue = Environment.getFileValue(""ProgramFiles(x86)""); // yes, I know it's not a file.
const pathValues = Environment.getPathValues(""Path"", "";"");
";
            var results = EvaluateExpressionsWithNoErrors(
                Spec,
                "newLine",
                "hasVariable",
                "stringValue",
                "pathValue",
                "fileValue",
                "pathValues");

            string programFileX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string userName = Environment.GetEnvironmentVariable("UserName");
            string path = Environment.GetEnvironmentVariable("Path");

            Assert.Equal(Environment.NewLine, results["newLine"]);
            Assert.Equal(programFileX86 != null, results["hasVariable"]);
            Assert.Equal(userName != null ? (object) userName : UndefinedValue.Instance, results["stringValue"]);
            Assert.Equal(
                programFileX86 != null ? (object) CreateAbsolutePath(programFileX86).Value : UndefinedValue.Instance,
                results["pathValue"]);
            Assert.Equal(
                programFileX86 != null ? (object) CreateSourceFile(programFileX86).Value : UndefinedValue.Instance,
                results["fileValue"]);

            if (path != null)
            {
                var paths = ArrayLiteral.CreateWithoutCopy(
                    path.Split(';').Select(p => EvaluationResult.Create(CreateAbsolutePath(p))).ToArray(),
                    default(LineInfo),
                    AbsolutePath.Invalid);
                Assert.IsAssignableFrom<ArrayLiteral>(results["pathValues"]);
                CheckUnorderedArray<AbsolutePath>(paths, results["pathValues"]);
            }
            else
            {
                Assert.Equal(UndefinedValue.Instance, results["pathValues"]);
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientSetUses()
        {
            const string Spec = @"
// Any change will break Office.
const paths = [p`D:/file/to/drop/x`, p`D:/file/to/drop/y`, p`D:/file/to/drop/z`];
const pathsExtra = [...paths, p`D:/file/to/drop/y`];
const pathSet = Set.empty<Path>().add(...pathsExtra);
const contains = pathSet.contains(p`D:/file/to/drop/x`);
const notContains = pathSet.contains(p`D:/file/to/drop/w`);
const toArray = pathSet.toArray();
";
            var results = EvaluateExpressionsWithNoErrors(
                Spec,
                "paths",
                "contains",
                "notContains",
                "toArray");

            CheckUnorderedArray<AbsolutePath>(results["paths"], results["toArray"]);
            Assert.True((bool) results["contains"]);
            Assert.False((bool) results["notContains"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientMapUses()
        {
            const string Spec = @"
// Any change will break Office.
const kvps: [PathAtom, File[]][] = [[a`x`, [f`D:/path1/x`, f`D:/path2/x`]], [a`y`, [f`D:/path/y`]]];
const map = Map.empty<PathAtom, File[]>().addRange([a`z`, [f`D:/path/z`]], ...kvps);
const containsKey = map.containsKey(a`y`);
const notContainsKey = map.containsKey(a`w`);
const x = map.get(a`x`);
const z = map.get(a`z`);
const w = map.get(a`w`);
";
            var results = EvaluateExpressionsWithNoErrors(
                Spec,
                "containsKey",
                "notContainsKey",
                "x",
                "z",
                "w");

            Assert.True((bool) results["containsKey"]);
            Assert.False((bool) results["notContainsKey"]);
            CheckArray<FileArtifact>(
                CreateArrayLiteral(new object[] {CreateSourceFile(@"D:\path1\x").Value, CreateSourceFile(@"D:\path2\x").Value}),
                results["x"]);
            CheckArray<FileArtifact>(
                CreateArrayLiteral(new object[] {CreateSourceFile(@"D:\path\z").Value}),
                results["z"]);
            Assert.Equal(UndefinedValue.Instance, results["w"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestAmbientTransformerUses()
        {
            const string Spec = @"
import {Transformer} from 'Sdk.Transformers';

// Any change will break Office.
const tool: Transformer.ToolDefinition = {
    exe: f`O:/path/to/tool.exe`,
    runtimeDependencies: [f`O:/path/to/tool.exe.config`],
    runtimeDirectoryDependencies: [Transformer.sealDirectory(d`O:/path/to/aux`, [f`O:/path/to/aux/dep.in`])],
    prepareTempDirectory: true,
    dependsOnWindowsDirectories: true,
    untrackedDirectoryScopes: [d`O:/dir/to/untracked`]
};
const sealedDir = Transformer.sealDirectory(d`O:/dir/subdirA`, [f`O:/dir/subdirA/1.txt`, f`O:/dir/subdirA/2.txt`]);
const sealedSourceDir = Transformer.sealSourceDirectory(d`O:/src/dev`, Transformer.SealSourceDirectoryOption.allDirectories);
const partiallySealedDir = Transformer.sealPartialDirectory(d`O:/dir/subdirB`, [f`O:/dir/subdirB/1.txt`, f`O:/dir/subdirB/2.txt`]);
const writtenFile = Transformer.writeFile(p`O:/out/dir/script.bat`, [""content1"", ""content2""]);
const execResult = Transformer.execute({
    tool: tool,
    description: ""O:/a/b/c/nmake_rule(10, 20)"",
    tags: [""platform:x64|configuration:debug"", ""noculture""],
    arguments: [{ name: undefined, value: ""start"" }],
    workingDirectory: d`.`,
    dependencies: [
        f`O:/src/file.txt`,
        writtenFile,
        sealedDir,
        partiallySealedDir
    ],
    implicitOutputs: [p`O:/out/dir/outputFile.txt`, d`O:/out/dir/outputDir`],
    environmentVariables: [
        { name: ""NAME1"", value: f`O:/some/path` },
        { name: ""NAME2"", value: ""SomeValue"" }
    ],
    additionalTempDirectories: [d`O:/temp/dir1`, d`O:/tmp/dir2`],
    unsafe: {
        untrackedScopes: [d`O:/dir/to/untracked2`],
        allowPreservedOutputs: false
    }
});
const outputFile = execResult.getOutputFile(p`O:/out/dir/outputFile.txt`);
const outputDir = execResult.getOutputDirectory(d`O:/out/dir/outputDir`);
";

            var results = EvaluateExpressionsWithNoErrors(
                Spec,
                "sealedDir",
                "sealedSourceDir",
                "partiallySealedDir",
                "writtenFile",
                "execResult",
                "outputFile",
                "outputDir");

            Assert.IsType<StaticDirectory>(results["sealedDir"]);
            Assert.IsType<StaticDirectory>(results["sealedSourceDir"]);
            Assert.IsType<StaticDirectory>(results["partiallySealedDir"]);
            Assert.Equal(CreateAbsolutePath(@"O:\dir\subdirA"), ((StaticDirectory) results["sealedDir"]).Path);
            Assert.Equal(CreateAbsolutePath(@"O:\src\dev"), ((StaticDirectory) results["sealedSourceDir"]).Path);
            Assert.Equal(CreateAbsolutePath(@"O:\dir\subdirB"), ((StaticDirectory) results["partiallySealedDir"]).Path);
            Assert.IsType<FileArtifact>(results["writtenFile"]);
            Assert.Equal(CreateOutputFile(@"O:\out\dir\script.bat"), results["writtenFile"]);
            Assert.IsAssignableFrom<ObjectLiteral>(results["execResult"]);
            Assert.IsType<FileArtifact>(results["outputFile"]);
            Assert.Equal(CreateOutputFile(@"O:\out\dir\outputFile.txt"), results["outputFile"]);
            Assert.IsType<StaticDirectory>(results["outputDir"]);
            Assert.Equal(CreateAbsolutePath(@"O:\out\dir\outputDir"), ((StaticDirectory) results["outputDir"]).Path);
        }

        private static void CheckUnorderedArray<T>(object expected, object actual)
        {
            var expectedArray = expected as ArrayLiteral;
            var actualArray = actual as ArrayLiteral;
            Assert.NotNull(expectedArray);
            Assert.NotNull(actualArray);
            Assert.Equal(expectedArray.Length, actualArray.Length);

            var actualPaths = new HashSet<T>();

            for (var i = 0; i < actualArray.Length; ++i)
            {
                Assert.IsType<T>(actualArray[i].Value);
                actualPaths.Add((T) actualArray[i].Value);
            }

            for (var i = 0; i < expectedArray.Length; ++i)
            {
                Assert.IsType<T>(expectedArray[i].Value);
                Assert.Contains((T) expectedArray[i].Value, actualPaths);
            }
        }

        private AbsolutePath CreateAbsolutePath(string path)
        {
            return AbsolutePath.Create(PathTable, path);
        }

        private EvaluationResult CreateSourceFile(string path)
        {
            return EvaluationResult.Create(FileArtifact.CreateSourceFile(CreateAbsolutePath(path)));
        }

        private FileArtifact CreateOutputFile(string path)
        {
            return FileArtifact.CreateOutputFile(CreateAbsolutePath(path));
        }
    }
}
