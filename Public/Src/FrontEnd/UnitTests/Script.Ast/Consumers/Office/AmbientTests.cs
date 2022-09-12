// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
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

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientContextUses()
        {
            var nugetCacheDirectory = X("/D/NuGetCache");

            string config = $@"
config({{
     mounts: [
        {{
            name: a`NuGetCache`,
            path: p`{nugetCacheDirectory}`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true
        }},
    ],
}});";
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
                .LegacyConfiguration(config)
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
            Assert.Equal(CreateAbsolutePath(nugetCacheDirectory), results["mountPath"]);
            Assert.Equal("name", (string)results["name"]);
            Assert.Equal("__Config__", (string)results["moduleName"]); // This test evaluates in the main config context for simplicity so that is why this returns a 'funny' module name.
            Assert.IsType<DirectoryArtifact>(results["userHomeDir"]);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
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

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientDebugUses()
        {
            const string Spec = @"
// Any change will break Office.
const x = Debug.writeLine(""Hello World!"");
";
            var result = EvaluateExpressionWithNoErrors(Spec, "x");
            Assert.Equal(UndefinedValue.Instance, result);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientPathQueryUses()
        {
            var testPath = X("/D/path/to/a/file.txt");
            var pathTo = X("/D/path/to");

            string spec = $@"
// Any change will break Office.
const path = p`{testPath}`;
const extension = path.extension;
const hasExtension = path.hasExtension;
const parent = path.parent;
const hasParent = path.hasParent;
const name = path.name;
const nameWithoutExtension = path.nameWithoutExtension;
const isWithin = path.isWithin(p`{pathTo}`);
const relative = p`{pathTo}`.getRelative(path);
";
            var results = EvaluateExpressionsWithNoErrors(
                spec,
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
            Assert.Equal(CreateAbsolutePath(X("/D/path/to/a")), results["parent"]);
            Assert.True((bool)results["hasParent"]);
            Assert.Equal(CreatePathAtom("file.txt"), results["name"]);
            Assert.Equal(CreatePathAtom("file"), results["nameWithoutExtension"]);
            Assert.True((bool)results["isWithin"]);
            Assert.Equal(CreateRelativePath("a/file.txt"), results["relative"]);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientPathUses()
        {
            var originalChangeExtensionPath = X("/D/path/to/a/file.in");
            var updatedChangeExtensionPath = X("/D/path/to/a/file.out");
            var originalPath = X("/D/a/b/foo.cs");
            var relocatedPath = X(@"/D/c/d/foo.cs");

            string spec = $@"
// Any change will break Office.
const changeExtension = p`{originalChangeExtensionPath}`.changeExtension("".out"");
const relocated = p`{originalPath}`.relocate(d`{X("/D/a/b")}`, d`{X("/D/c/d")}`);
";
            var results = EvaluateExpressionsWithNoErrors(spec, "changeExtension", "relocated");
            Assert.Equal(CreateAbsolutePath(updatedChangeExtensionPath), results["changeExtension"]);
            Assert.Equal(CreateAbsolutePath(relocatedPath), results["relocated"]);
        }
        
        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientDirectoryUses()
        {
            var directoryPath = X("/D/path/to/a/directory");

            string spec = $@"
// Any change will break Office.
const dir = d`{directoryPath}`;
const dirPath = dir.path;
";
            var results = EvaluateExpressionsWithNoErrors(spec, "dir", "dirPath");

            Assert.IsType<DirectoryArtifact>(results["dir"]);
            Assert.Equal(CreateAbsolutePath(directoryPath), results["dirPath"]);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
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

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
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

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientStaticDirectoryUses()
        {
            var sealDirectoryRoot = X("/D/path/to/dir");
            var pathA = X("/D/path/to/dir/a.txt");
            var pathB = X("/D/path/to/dir/b.txt");
            var pathC = X("/D/path/to/dir/subdir/c.txt");

            string spec = $@"
import {{Transformer}} from 'Sdk.Transformers';

// Any change will break Office.
const sealedDir = Transformer.sealDirectory(d`{sealDirectoryRoot}`, [ f`{pathA}`, f`{pathB}`, f`{pathC}`]);
const dirPath = sealedDir.root.path;
const contents = sealedDir.contents;
";
            var results = EvaluateExpressionsWithNoErrors(spec, "dirPath", "contents");
            Assert.Equal(CreateAbsolutePath(sealDirectoryRoot), results["dirPath"]);
            Assert.IsAssignableFrom<ArrayLiteral>(results["contents"]);
            var arrayContent = results["contents"] as ArrayLiteral;
            Assert.NotNull(arrayContent);
            Assert.Equal(3, arrayContent.Length);
            Assert.Equal(CreateSourceFile(pathA), arrayContent[0]);
            Assert.Equal(CreateSourceFile(pathB), arrayContent[1]);
            Assert.Equal(CreateSourceFile(pathC), arrayContent[2]);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestPathInterpolationUses()
        {
            var rootDirectoryPath = X("/D/path/to/a/dir");

            string spec = $@"
// Any change will break Office.
const dir = d`{X("/D/path/to/a/dir")}`;
const fileName = a`file.txt`;
const relative = r`subdir/subsubdir`;
const name = ""file"";
const relativeString = ""subdir/subsubdir"";
const iPath1 = p`${{dir}}/x/${{relative}}/y/${{fileName}}`;
const iPath2 = p`${{dir}}/x/${{relativeString}}/y/${{name + "".txt""}}`;
";
            var results = EvaluateExpressionsWithNoErrors(spec, "iPath1", "iPath2");
            var path = CreateAbsolutePath(X("/D/path/to/a/dir/x/subdir/subsubdir/y/file.txt"));

            Assert.Equal(path, results["iPath1"]);
            Assert.Equal(path, results["iPath2"]);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
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

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
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

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
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
            string spec = @"
// Any change will break Office.
const newLine = Environment.newLine();
";

            if (OperatingSystemHelper.IsWindowsOS)
            {
               spec += @"const hasVariable = Environment.hasVariable(""ProgramFiles(x86)"");
const stringValue = Environment.getStringValue(""UserName"");
const pathValue = Environment.getPathValue(""ProgramFiles(x86)"");
const fileValue = Environment.getFileValue(""ProgramFiles(x86)""); // yes, I know it's not a file.
const pathValues = Environment.getPathValues(""Path"", "";"");
";
            }
            else
            {
                spec += @"const hasVariable = Environment.hasVariable(""HOME"");
const stringValue = Environment.getStringValue(""USER"");
const pathValue = Environment.getPathValue(""HOME"");
const fileValue = Environment.getFileValue(""HOME)""); // yes, I know it's not a file.
const pathValues = Environment.getPathValues(""PATH"", "":"");
";
            }    

            var results = EvaluateExpressionsWithNoErrors(
                spec,
                "newLine",
                "hasVariable",
                "stringValue",
                "pathValue",
                "fileValue",
                "pathValues");

            string testPath = Environment.GetEnvironmentVariable(OperatingSystemHelper.IsWindowsOS ? "ProgramFiles(x86)" : "HOME");
            string userName = Environment.GetEnvironmentVariable(OperatingSystemHelper.IsWindowsOS ? "UserName" : "USER");
            string path = Environment.GetEnvironmentVariable(OperatingSystemHelper.IsWindowsOS ? "Path" : "PATH");

            Assert.Equal(Environment.NewLine, results["newLine"]);
            Assert.Equal(testPath != null, results["hasVariable"]);
            Assert.Equal(userName != null ? (object) userName : UndefinedValue.Instance, results["stringValue"]);
            // TODO: investigate why the result is a Path type instead of a Name type on Linux when comparing the value
            Assert.Equal(
                testPath != null ? (object) CreateAbsolutePath(testPath).Value : UndefinedValue.Instance,
                results["pathValue"]);
            Assert.Equal(
                testPath != null ? (object) CreateSourceFile(testPath).Value : UndefinedValue.Instance,
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

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientSetUses()
        {
            string spec = $@"
// Any change will break Office.
const paths = [p`{X("/D/file/to/drop/x")}`, p`{X("/D/file/to/drop/y")}`, p`{X("/D/file/to/drop/z")}`];
const pathsExtra = [...paths, p`{X("/D/file/to/drop/y")}`];
const pathSet = Set.empty<Path>().add(...pathsExtra);
const contains = pathSet.contains(p`{X("/D/file/to/drop/x")}`);
const notContains = pathSet.contains(p`{X("/D/file/to/drop/w")}`);
const toArray = pathSet.toArray();
";
            var results = EvaluateExpressionsWithNoErrors(
                spec,
                "paths",
                "contains",
                "notContains",
                "toArray");

            CheckUnorderedArray<AbsolutePath>(results["paths"], results["toArray"]);
            Assert.True((bool) results["contains"]);
            Assert.False((bool) results["notContains"]);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientMapUses()
        {
            string spec = $@"
// Any change will break Office.
const kvps: [PathAtom, File[]][] = [[a`x`, [f`{X("/D/path1/x")}`, f`{X("/D/path2/x")}`]], [a`y`, [f`{X("/D/path/y")}`]]];
const map = Map.empty<PathAtom, File[]>().addRange([a`z`, [f`{X("/D/path/z")}`]], ...kvps);
const containsKey = map.containsKey(a`y`);
const notContainsKey = map.containsKey(a`w`);
const x = map.get(a`x`);
const z = map.get(a`z`);
const w = map.get(a`w`);
";
            var results = EvaluateExpressionsWithNoErrors(
                spec,
                "containsKey",
                "notContainsKey",
                "x",
                "z",
                "w");

            Assert.True((bool) results["containsKey"]);
            Assert.False((bool) results["notContainsKey"]);
            CheckArray<FileArtifact>(
                CreateArrayLiteral(new object[] {CreateSourceFile(X("/D/path1/x")).Value, CreateSourceFile(X("/D/path2/x")).Value}),
                results["x"]);
            CheckArray<FileArtifact>(
                CreateArrayLiteral(new object[] {CreateSourceFile(X("/D/path/z")).Value}),
                results["z"]);
            Assert.Equal(UndefinedValue.Instance, results["w"]);
        }

        [FactIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
        public void TestAmbientTransformerUses()
        {
            string spec = $@"
import {{Transformer}} from 'Sdk.Transformers';

// Any change will break Office.
const tool: Transformer.ToolDefinition = {{
    exe: f`{X("/O/path/to/tool.exe")}`,
    runtimeDependencies: [f`{X("/O/path/to/tool.exe.config")}`],
    runtimeDirectoryDependencies: [Transformer.sealDirectory(d`{X("/O/path/to/aux")}`, [f`{X("/O/path/to/aux/dep.in")}`])],
    prepareTempDirectory: true,
    dependsOnWindowsDirectories: true,
    untrackedDirectoryScopes: [d`{X("/O/dir/to/untracked")}`]
}};
const sealedDir = Transformer.sealDirectory(d`{X("/O/dir/subdirA")}`, [f`{X("/O/dir/subdirA/1.txt")}`, f`{X("/O/dir/subdirA/2.txt")}`]);
const sealedSourceDir = Transformer.sealSourceDirectory(d`{X("/O/src/dev")}`, Transformer.SealSourceDirectoryOption.allDirectories);
const partiallySealedDir = Transformer.sealPartialDirectory(d`{X("/O/dir/subdirB")}`, [f`{X("/O/dir/subdirB/1.txt")}`, f`{X("/O/dir/subdirB/2.txt")}`]);
const writtenFile = Transformer.writeFile(p`{X("/O/out/dir/script.bat")}`, [""content1"", ""content2""]);
const execResult = Transformer.execute({{
    tool: tool,
    description: ""{X("/O/a/b/c/nmake_rule")}(10, 20)"",
    tags: [""platform:x64|configuration:debug"", ""noculture""],
    arguments: [{{ name: undefined, value: ""start"" }}],
    workingDirectory: d`.`,
    dependencies: [
        f`{X("/O/src/file.txt")}`,
        writtenFile,
        sealedDir,
        partiallySealedDir
    ],
    implicitOutputs: [p`{X("/O/out/dir/outputFile.txt")}`, d`{X("/O/out/dir/outputDir")}`],
    environmentVariables: [
        {{ name: ""NAME1"", value: f`{X("/O/some/path")}` }},
        {{ name: ""NAME2"", value: ""SomeValue"" }}
    ],
    additionalTempDirectories: [d`{X("/O/temp/dir1")}`, d`{X("/O/tmp/dir2")}`],
    unsafe: {{
        untrackedScopes: [d`{X("/O/dir/to/untracked2")}`],
        allowPreservedOutputs: false
    }}
}});
const outputFile = execResult.getOutputFile(p`{X("/O/out/dir/outputFile.txt")}`);
const outputDir = execResult.getOutputDirectory(d`{X("/O/out/dir/outputDir")}`);
";
            var results = EvaluateExpressionsWithNoErrors(
                spec,
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
            Assert.Equal(CreateAbsolutePath(X(@"/O/dir/subdirA")), ((StaticDirectory) results["sealedDir"]).Path);
            Assert.Equal(CreateAbsolutePath(X(@"/O/src/dev")), ((StaticDirectory) results["sealedSourceDir"]).Path);
            Assert.Equal(CreateAbsolutePath(X(@"/O/dir/subdirB")), ((StaticDirectory) results["partiallySealedDir"]).Path);
            Assert.IsType<FileArtifact>(results["writtenFile"]);
            Assert.Equal(CreateOutputFile(X(@"/O/out/dir/script.bat")), results["writtenFile"]);
            Assert.IsAssignableFrom<ObjectLiteral>(results["execResult"]);
            Assert.IsType<FileArtifact>(results["outputFile"]);
            Assert.Equal(CreateOutputFile(X(@"/O/out/dir/outputFile.txt")), results["outputFile"]);
            Assert.IsType<StaticDirectory>(results["outputDir"]);
            Assert.Equal(CreateAbsolutePath(X(@"/O/out/dir/outputDir")), ((StaticDirectory) results["outputDir"]).Path);
        }

        /// <summary>
        /// Most tests are in <see cref="InterpretStringOperations.TestStringVariableExpansionInWindows(string, string)"/> since the implementation 
        /// is shared. Here we focus on path validation logic.
        /// </summary>
        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData("C:/path/%MyEnvVar%/foo", "C:/path/MyValue/foo", true)]
        [InlineData("C:/path/%MyEnvVarWithInvalidChars%/foo", "", false)]
        public void TestPathVariableExpansion(string unexpanded, string expanded, bool expectSuccess)
        {
            Environment.SetEnvironmentVariable("MyEnvVar", "MyValue");
            Environment.SetEnvironmentVariable("MyEnvVarWithInvalidChars", "MyOther:Value");

            string expression = $"const s: Path = Environment.expandEnvironmentVariablesInPath(p`{unexpanded}`);";

            if (expectSuccess)
            {
                var result = EvaluateExpressionsWithNoErrors(expression, "s");
                Assert.Equal(AbsolutePath.Create(PathTable, expanded), result["s"]);
            }
            else
            {
                var error = EvaluateWithFirstError(expression, "s");
                Assert.Equal((int)global::BuildXL.FrontEnd.Script.Tracing.LogEventId.InvalidPathOperation, error.ErrorCode);
            }
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData("%MyEnvVar%", "MyValue")]
        [InlineData("%MyEnvVar%WithATail", "MyValueWithATail")]
        [InlineData("WithAHead%MyEnvVar%WithATail", "WithAHeadMyValueWithATail")]
        [InlineData("WithAHead%MyEnvVar%WithATail%MyOtherEnvVar%WithAnotherTail", "WithAHeadMyValueWithATailMyOtherValueWithAnotherTail")]
        [InlineData("%NotDefinedVar%", "%NotDefinedVar%")]
        [InlineData("%MyEnvVar%With%NotDefinedVar%", "MyValueWith%NotDefinedVar%")]
        [InlineData("%MyEnvVar%%MyEnvVar%", "MyValueMyValue")]
        [InlineData("%MyEnvVar", "%MyEnvVar")]
        [InlineData("%%", "%%")]
        [InlineData("%", "%")]
        public void TestStringVariableExpansionInWindows(string unexpanded, string expanded)
        {
            Environment.SetEnvironmentVariable("MyEnvVar", "MyValue");
            Environment.SetEnvironmentVariable("MyOtherEnvVar", "MyOtherValue");

            var result = EvaluateExpressionsWithNoErrors($"const s: string = Environment.expandEnvironmentVariablesInString('{unexpanded}');", "s");

            Assert.Equal(expanded, result["s"]);
        }

        [TheoryIfSupported(requiresUnixBasedOperatingSystem: true)]
        [InlineData("$MyEnvVar", "MyValue")]
        [InlineData("$MyEnvVar/WithATail", "MyValue/WithATail")]
        [InlineData("WithAHead$MyEnvVar/WithATail", "WithAHeadMyValue/WithATail")]
        [InlineData("WithAHead($MyEnvVar)WithATail($MyOtherEnvVar)WithAnotherTail", "WithAHead(MyValue)WithATail(MyOtherValue)WithAnotherTail")]
        [InlineData("$NotDefinedVar", "$NotDefinedVar")]
        [InlineData("$MyEnvVar/With$NotDefinedVar", "MyValue/With$NotDefinedVar")]
        [InlineData("$MyEnvVar$MyEnvVar", "MyValueMyValue")]
        [InlineData("$", "$")]
        public void TestStringVariableExpansionInUnix(string unexpanded, string expanded)
        {
            Environment.SetEnvironmentVariable("MyEnvVar", "MyValue");
            Environment.SetEnvironmentVariable("MyOtherEnvVar", "MyOtherValue");

            var result = EvaluateExpressionsWithNoErrors($"const s: string = Environment.expandEnvironmentVariablesInString('{unexpanded}');", "s");

            Assert.Equal(expanded, result["s"]);
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
