// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class InterpretPath : DsTest
    {
        public InterpretPath(ITestOutputHelper output)
            : base(output)
        {
        }

        // The constructed path is perfectly valid on Unix systems, thus only run this on Windows
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void MissingDollarSignInInterpolatedPathShouldLeadToInvalidPathExpression()
        {
            string code =
                @"function f(format: string, ...args: any[]) : string {
return ""hi"";
}
export const x = p`he{f(""hi"")}llo`;
";

            var result = EvaluateWithFirstError(code);

            Assert.Equal(LogEventId.InvalidPathExpression, (LogEventId) result.ErrorCode);
        }

        [TheoryIfSupported(requiresUnixBasedOperatingSystem: true)]
        [InlineData("p")]
        [InlineData("d")]
        [InlineData("f")]
        public void InterpolatedRootedPathIsInterpretedAsAbsolutePath(string factoryName)
        {
            string code = $@"const aPath = {factoryName}`/rooted/path/${{r`with/some`}}/interpolation`.path;";

            var result = EvaluateExpressionWithNoErrors(code, "aPath");

            Assert.Equal(result, AbsolutePath.Create(PathTable, "/rooted/path/with/some/interpolation"));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void InterpolatedRelativePathIsInterpretedAsRelativePath()
        {
            string code = $@"const aPath = r`/rooted/path/${{r`with/some`}}/interpolation`;";

            var result = EvaluateExpressionWithNoErrors(code, "aPath");

            Assert.Equal(result, RelativePath.Create(StringTable, "/rooted/path/with/some/interpolation"));
        }

        [Fact]
        public void CreatePathWithPBacktick()
        {
            string spec = @"
namespace M {
  export const pp: Path = p`foo.cs`;
  export const extension = pp.extension;
  export const name = pp.nameWithoutExtension;
}";

            var result = EvaluateExpressionsWithNoErrors(spec, "M.extension", "M.name");
            Assert.Equal(".cs", result.Get<PathAtom>("M.extension").ToString(StringTable));
            Assert.Equal("foo", result.Get<PathAtom>("M.name").ToString(StringTable));
        }

        [Fact]
        public void TestGetExtensionAndGetName()
        {
            string spec = @"
namespace M {
  const p1: Path = p`foo.cs`;
  export const extension = p1.extension;
  export const name = p1.nameWithoutExtension;
}";

            var result = EvaluateExpressionsWithNoErrors(spec, "M.extension", "M.name");
            Assert.Equal(".cs", result.Get<PathAtom>("M.extension").ToString(StringTable));
            Assert.Equal("foo", result.Get<PathAtom>("M.name").ToString(StringTable));
        }

        [Fact]
        public void TestPathChangeExtension()
        {
            string spec = @"
namespace M {
  const p1: Path = p`foo.cs`;
  const p2 = p1.changeExtension("".js"");
  const p3: Path = p`foo.js`;
  export const result = (p2 === p3);
}";

            var result = EvaluateExpressionWithNoErrors(spec, "M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathChangeExtensionEmpty()
        {
            var code = @"
namespace M
{
  const p1: Path = p`foo.cs`;
  const p2 = p1.changeExtension("""");
  const p3: Path = p`foo`;
  export const result = (p2 === p3);
}
";

            var result = EvaluateExpressionWithNoErrors(code, "M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathGetExtension()
        {
            var code = @"
namespace M
{
  const p1: Path = p`foo.cs`;
  const p2 = p1.extension;
  const p3: PathAtom = PathAtom.create("".cs"");
  export const result = (p2 === p3);
}
";

            var result = EvaluateExpressionWithNoErrors(code, "M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathHasExtension()
        {
            var code = @"
namespace M
{
  const p1: Path = p`foo.cs`;
  export const r1 = p1.hasExtension;

  const p2: Path = p`foo`;
  export const r2 = p2.hasExtension;
}
";

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");
            Assert.Equal(true, result["M.r1"]);
            Assert.Equal(false, result["M.r2"]);
        }

        [Fact]
        public void TestPathGetParent()
        {
            var code = String.Format(@"
namespace M
{{
  const p1: Path = p`a/b/foo.cs`;
  const p2 = p1.parent;
  const p3: Path = p`a/b`;
  export const r1 = (p2 === p3);

  const p4: Path = p`{0}`;
  const p5 = p4.parent;
  export const r2 = (p5 === undefined);
}}
", OperatingSystemHelper.IsUnixOS ? "/" : "c:/");

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");
            Assert.Equal(true, result["M.r1"]);
            Assert.Equal(true, result["M.r2"]);
        }

        [Fact]
        public void TestPathHasParent()
        {
            var code = String.Format(@"
namespace M
{{
  const p1: Path = p`a/b/foo.cs`;
  export const r1 = p1.hasParent;

  const p2: Path = p`{0}`;
  export const r2 = p2.hasParent;

}}
", OperatingSystemHelper.IsUnixOS ? "/" : "c:/");

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");
            Assert.Equal(true, result["M.r1"]);
            Assert.Equal(false, result["M.r2"]);
        }

        [Fact]
        public void TestPathGetName()
        {
            var code = String.Format(@"
namespace M
{{
  const p1: Path = p`{0}a/b/foo.cs`;
  const name1 = p1.name;

  const name2: PathAtom = PathAtom.create(""foo.cs"");
  export const result = name1 === name2;

}}
", OperatingSystemHelper.IsUnixOS ? "/" : "c:/");

            var result = EvaluateExpressionWithNoErrors(code, "M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathGetNameWithoutExtension()
        {
            var code = String.Format(@"
namespace M
{{
  const p1: Path = p`{0}a/b/foo.cs`;
  export const result = p1.name.changeExtension("""") === p1.nameWithoutExtension;

}}", OperatingSystemHelper.IsUnixOS ? "/" : "c:/");

            var result = EvaluateExpressionWithNoErrors(code, "M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathGetIsWithin()
        {
            var code = @"
namespace M
{
  const p1: Path = p`a/b/foo.cs`;
  const p2: Path = p`a/b`;

  export const r1 = p1.isWithin(p2);

  const p3: Path = p`a/c`;

  export const r2 = p1.isWithin(p3);
}";

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");
            Assert.Equal(true, result["M.r1"]);
            Assert.Equal(false, result["M.r2"]);
        }

        [Fact]
        public void TestPathGetPathRoot()
        {
            var code = String.Format(@"
namespace M
{{
  const p1: Path = p`{0}a/b/foo.cs`;
  const p2: Path = p`{1}a/b`;

  export const r1 = p1.pathRoot;
  export const r2 = p2.pathRoot;
}}", OperatingSystemHelper.IsUnixOS ? "/" : "x:/", OperatingSystemHelper.IsUnixOS ? "/" : "c:/");

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");
            Assert.Equal(CreatePath(A("x")), result["M.r1"]);
            Assert.Equal(CreatePath(A("c")), result["M.r2"]);
        }

        [Fact]
        public void TestWindowsPathSplittingBug936655()
        {
            var code = String.Format(@"
const p1: Path = p`{0}a/b`;

export const root = p1.pathRoot;
export const ntDrive = root.name;
export const ntRoot = root.getRelative(p1);
", OperatingSystemHelper.IsUnixOS ? "/" : "x:/");

            var result = EvaluateExpressionsWithNoErrors(code, "ntDrive", "ntRoot");
            Assert.Equal(CreateRelativePath("a" + Path.DirectorySeparatorChar + "b"), result["ntRoot"]);
            Assert.Equal(OperatingSystemHelper.IsUnixOS ? String.Empty : "x:", ((PathAtom) result["ntDrive"]).ToString(FrontEndContext.StringTable));
        }

        [Fact]
        public void TestPathGetIsWithinDirectory()
        {
            var code = @"
import {Transformer} from 'Sdk.Transformers';

namespace M
{
  const pp: Path = p`a/b/foo.cs`;
  const dir: StaticDirectory = Transformer.sealDirectory(d`a/b`, [pp]);

  export const result = pp.isWithin(dir);
}";

            var result = EvaluateExpressionWithNoErrors(code, "M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathRelocate()
        {
            var code = @"
namespace M
{
  const pp: Path = p`a/b/foo.cs`;
  const source: Directory = d`a/b`;
  const target: Directory = d`c/d`;
  const relocated = pp.relocate(source, target);

  const expected = p`c/d/foo.cs`;

  export const result = relocated === expected;
}";

            var result = EvaluateExpressionWithNoErrors(code, "M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathRelocateDirectory()
        {
            var code = @"
import {Transformer} from 'Sdk.Transformers';

namespace M
{
  const p1: Path = p`a/b/foo.cs`;
  const p2: Path = p`c/d/bar.cs`;
  const d1: StaticDirectory = Transformer.sealPartialDirectory(d`a/b`, [p1]);
  const d2: StaticDirectory = Transformer.sealPartialDirectory(d`c/d`, [p2]);

  const relocated = p1.relocate(d1, d2);
  const expected = p`c/d/foo.cs`;

  export const result = relocated === expected;
}";

            var result = EvaluateExpressionWithNoErrors(code, "M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathRelocateAndChangeExtension()
        {
            var code = @"
namespace M
{
  const pp: Path = p`a/b/foo.cs`;
  const source: Directory = d`a/b`;
  const target: Directory = d`c/d`;
  const relocated = pp.relocate(source, target, "".js"");

  const expected = p`c/d/foo.js`;

  export const result = relocated === expected;
}";

            var result = EvaluateExpressionWithNoErrors(code, "M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathRelocateNotWithinSource()
        {
            var code = @"
namespace M
{
  const pp: Path = p`a/b/foo.cs`;
  const target: Directory = d`a/b`;
  const source: Directory = d`c/d`;
  const relocated = pp.relocate(source, target, "".js"");
}";

            var result = EvaluateWithFirstError(code);
            Assert.Equal(LogEventId.InvalidPathOperation, (LogEventId) result.ErrorCode);
        }

        [Fact]
        public void TestPathGetRelative()
        {
            var code = @"
namespace M
{
  const pp: Path = p`a/b/c`;
  const relative1: Path = p`a/b/c/d/e`;
  const relative2: Path = p`an/unrelated/path`;

  const pRelative1 = pp.getRelative(relative1);
  export const result1 = pRelative1 === RelativePath.create(""d/e"");
  export const result2 = pRelative1.toPathAtoms()[1] === PathAtom.create(""e"");
  export const result3 = pp.getRelative(relative2) === undefined;
}";

            var result = EvaluateExpressionsWithNoErrors(code, "M.result1", "M.result2", "M.result3");
            Assert.Equal(true, result["M.result1"]);
            Assert.Equal(true, result["M.result2"]);
            Assert.Equal(true, result["M.result3"]);
        }

        [Fact]
        public void TestPathCombine()
        {
            var code = @"
namespace M
{
  const pp: Path = p`a/b/c`;
  export const r1 = pp.combine(""foo.cs"") === p`a/b/c/foo.cs`;
  export const r2 = pp.combine(""d/e/foo.cs"") === p`a/b/c/d/e/foo.cs`;
}";

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");
            Assert.Equal(true, result["M.r1"]);
            Assert.Equal(true, result["M.r2"]);
        }

        [Fact]
        public void TestPathCombinePaths()
        {
            string code = String.Format(@"
const docs = p`{0}Docs`;
const foo = docs.combinePaths(...[""1"", ""2""]);
export const r = foo.toString();
", OperatingSystemHelper.IsUnixOS ? "/" : "C:/");

            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(String.Format("p`{0}Docs/1/2`", OperatingSystemHelper.IsUnixOS ? "/" : "C:/"), result);
        }

        [Fact]
        public void TestPathGetPath()
        {
            var code = @"
   const pp: Path = p`a/b/c`;
   export const result = pp.path === p`a/b/c`;
";

            var result = EvaluateExpressionWithNoErrors(code, "result");

            Assert.Equal(true, result);
        }

        [Fact]
        public void TestPathFragment()
        {
            var code = @"
namespace M
{
    const args = { bondFile: p`path/to/bondFile` };
    const bondFileWithoutExtension = args.bondFile.nameWithoutExtension;
    export const foo = bondFileWithoutExtension.concat(""_proxies.cs"");
}";

            var result = EvaluateExpressionWithNoErrors(code, "M.foo");
            Assert.Equal("bondFile_proxies.cs", ((PathAtom) result).ToString(StringTable));
        }

        [Fact]
        public void TestPathAndDirectory()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace MyProject
{
    const x = Transformer.sealDirectory(d`dirs`, glob(d`dirs`, ""*""));
    const y = p`dirs/a.txt`;
    const z = y.parent;

    export const v0 = (y === x.getFile(y).path);
    export const v1 = (z === y.parent);
}");

            testWriter.AddExtraFile(@"MyProject/dirs/a.txt", "Awesome");
            testWriter.AddExtraFile(@"MyProject/dirs/b.txt", "Super");

            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] {"MyProject.v0", "MyProject.v1"});

            result.ExpectNoError();
            result.ExpectValues(count: 2);

            Assert.Equal(true, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
        }

        [Fact]
        public void TestPathExtend()
        {
            var code = @"
namespace M
{
  const path: Path = p`a/b/Foo`;
  const barStr: string = ""Bar"";
  const barAtom: PathAtom = PathAtom.create(barStr);
  const pathBar: Path = p`a/b/FooBar`;

  export const r1 = path.extend(barStr) === pathBar;
  export const r2 = path.extend(barAtom) === pathBar;
}";

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");
            Assert.Equal(true, result["M.r1"]);
            Assert.Equal(true, result["M.r2"]);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestManyFormsOfAbsolutePaths()
        {
            var code = @"
namespace M
{
  export const p1: Path = p`/??/C:/absolute/path/to/file.txt`;
  export const p2: Path = p`//?/C:/absolute/path/to/file.txt`;
  export const p3: Path = p`//unc/path/to/file.txt`;
  export const s1 = p1.toString();
  export const s2 = p2.toString();
  export const s3 = p3.toString();
}
";
            var result = EvaluateExpressionsWithNoErrors(code, "M.p1", "M.p2", "M.p3", "M.s1", "M.s2", "M.s3");

            Assert.Equal(AbsolutePath.Create(PathTable, @"C:\absolute\path\to\file.txt"), result["M.p1"]);
            Assert.Equal(AbsolutePath.Create(PathTable, @"C:\absolute\path\to\file.txt"), result["M.p2"]);
            Assert.Equal(AbsolutePath.Create(PathTable, @"\\unc\path\to\file.txt"), result["M.p3"]);

            Assert.Equal("p`C:/absolute/path/to/file.txt`", result["M.s1"]);
            Assert.Equal("p`C:/absolute/path/to/file.txt`", result["M.s2"]);
            Assert.Equal("p`//unc/path/to/file.txt`", result["M.s3"]);
        }

        [Fact]
        public void TestConcatenatingPath()
        {
            var code = @"
namespace M
{
  const p1: Path = p`foo`;
  const p2: Path = p`foobar`;
  const p3: Path = p1.concat(a`bar`);
  const p4: Path = p1.extend(a`bar`);
  export const result1 = (p2 === p3);
  export const result2 = (p2 === p4);
}
";
            var result = EvaluateExpressionsWithNoErrors(code, "M.result1", "M.result2");
            Assert.Equal(true, result["M.result1"]);
            Assert.Equal(true, result["M.result2"]);
        }

        // No support for Module relative paths on Unix systems as '/' denotes every absolute path
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestModuleRelativePath()
        {
            const string Config = "config({});";

            string packageConfigA = $@"
module({{
    name: ""PackA"",
}});
";
            var code = @"
namespace M
{
  const p1: Path = p`/foo.txt`;
  const p2: Path = p`../foo.txt`;
  export const result = (p1 === p2);
}";

            var result = Build()
                .LegacyConfiguration(Config)
                .AddSpec(@"PackA/package.config.dsc", packageConfigA)
                .AddSpec(@"PackA/SubFolder/package.dsc", code)
                .RootSpec(@"PackA/SubFolder/package.dsc")
                .EvaluateExpressionWithNoErrors("M.result");

            Assert.Equal(true, result);
        }
    }
}
