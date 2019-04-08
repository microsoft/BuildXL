// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientFileTests : DsTest
    {
        public AmbientFileTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestFileGetExtension()
        {
            var spec = @"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const f: DerivedFile = Transformer.writeFile(p`a.txt`, [""Awesome!""]);
      
    const r1: PathAtom = f.extension; 
    const r2: PathAtom = PathAtom.create("".txt"");
    export const result = (r1 === r2);
}";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void FileExistsIfAdded()
        {
            var spec = @"
export const result = File.exists(f`a.txt`);
";
            var result = Build().AddSpec(spec).AddFile("a.txt", "").EvaluateExpressionWithNoErrors("result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void FileDoesNotExists()
        {
            var spec = @"
export const result = File.exists(f`a.txt`);
";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("result");
            Assert.Equal(false, result);
        }

        [Fact]
        public void TestFileHasExtension()
        {
            var spec = @"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const f1: DerivedFile = Transformer.writeFile(p`a.txt`, [""Awesome!""]);
    export const r1 = f1.hasExtension;

    const f2: DerivedFile = Transformer.writeFile(p`a`, [""Awesome!""]);
    export const r2 = f2.hasExtension;

}";
            var result = Build().AddSpec(spec).EvaluateExpressionsWithNoErrors("M.r1", "M.r2");

            Assert.Equal(true, result.Values[0]);
            Assert.Equal(false, result.Values[1]);
        }

        [Fact]
        public void TestFileGetParent()
        {
            var spec = @"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const f1: DerivedFile = Transformer.writeFile(p`a/b/foo.cs`, [""Awesome!""]);
    const p2 = f1.parent;
    const p3: Path = p`a/b`;
    export const r1 = (p2 === p3);
}";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("M.r1");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestFilehHasParent()
        {
            var spec = @"
import {Transformer} from 'Sdk.Transformers';

namespace M 
{
  const f1: DerivedFile = Transformer.writeFile(p`a/b/foo.cs`, [""Awesome!""]);
  export const r1 = f1.hasParent;
}";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("M.r1");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestFileGetName()
        {
            var spec = @"
import {Transformer} from 'Sdk.Transformers';

namespace M 
{
    const f1: DerivedFile = Transformer.writeFile(p`a/b/foo.cs`, [""Awesome!""]);
    const name1 = f1.name;
  
    const name2: PathAtom = PathAtom.create(""foo.cs"");
    export const result = name1 === name2;
}";
            var value = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(true, value);
        }

        [Fact]
        public void TestFileGetNameWithoutExtension()
        {
            var spec = @"
import {Transformer} from 'Sdk.Transformers';

namespace M 
{
  const f1: DerivedFile = Transformer.writeFile(p`a/b/foo.cs`, [""Awesome!""]);
  export const result = f1.name.changeExtension("""") === f1.nameWithoutExtension;
}";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("M.result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void TestFileIsWithin()
        {
            var spec = @"
import {Transformer} from 'Sdk.Transformers';

namespace M 
{
  const f1: DerivedFile = Transformer.writeFile(p`a/b/foo.cs`, [""Awesome!""]);
  const p1: Path = p`a/b`;

  export const r1 = f1.isWithin(p1);

  const p2: Path = p`a/c`;

  export const r2 = f1.isWithin(p2);
}";
            var result = Build().AddSpec(spec).EvaluateExpressionsWithNoErrors("M.r1", "M.r2");
            Assert.Equal(true, result.Values[0]);
            Assert.Equal(false, result.Values[1]);
        }

        [Fact]
        public void TestFileGetRelative()
        {
            var spec = @"
import {Transformer} from 'Sdk.Transformers';

namespace M 
{
  const f1: DerivedFile = Transformer.writeFile(p`a/b/c`, [""Awesome!""]);
  const relative1: Path = p`a/b/c/d/e`;
  const relative2: Path = p`an/unrelated/path`;

  const f1Relative1 = f1.getRelative(relative1);
  export const r1 = f1Relative1 === RelativePath.create(""d/e"");
  export const r2 = f1Relative1.toPathAtoms()[1] === PathAtom.create(""e"");
  export const r3 = f1.getRelative(relative2) === undefined;
}";
            var result = Build().AddSpec(spec).EvaluateExpressionsWithNoErrors("M.r1", "M.r2", "M.r3");
            Assert.Equal(true, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
            Assert.Equal(true, result.Values[2]);
        }

        [Fact]
        public void TestFileGetPath()
        {
            var spec = @"
import {Transformer} from 'Sdk.Transformers';

namespace M 
{
  const f1: DerivedFile = Transformer.writeFile(p`a/b/foo.cs`, [""Awesome!""]);
  const p1: Path = p`a/b/foo.cs`;

  export const r1 = f1.path === p1;
}";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("M.r1");
            Assert.Equal(true, result);
        }


        [Fact]
        public void GetToDiangosticString()
        {
            var spec = @"
const result: string = f`a/b/foo.cs`.toDiagnosticString();
";
            var result = (string)Build().AddSpec(spec).EvaluateExpressionWithNoErrors("result");
            AssertCanonicalEquality(@"a\b\foo.cs", result);
        }
    }
}
