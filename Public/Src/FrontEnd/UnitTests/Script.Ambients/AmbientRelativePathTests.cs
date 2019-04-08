// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientRelativePathTests : DsTest
    {
        public AmbientRelativePathTests(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void TestRelativePathChangeExtension()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p1: RelativePath = RelativePath.create(""path/to/foo.cs"");
  const p2 = p1.changeExtension("".js"");
  const p3: RelativePath = RelativePath.create(""path/to/foo.js"");
  export const result = p2 === p3;
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(true, result.Values[0]);
        }

        [Fact]
        public void TestRelativePathChangeExtensionEmpty()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p1: RelativePath = RelativePath.create(""path/to/foo.cs"");
  const p2 = p1.changeExtension("""");
  const p3: RelativePath = RelativePath.create(""path/to/foo"");
  export const result = p2 === p3;
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(true, result.Values[0]);
        }

        [Fact]
        public void TestRelativePathGetExtension()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p1: RelativePath = RelativePath.create(""path/to/foo.cs"");
  const a1: PathAtom = p1.extension;
  const a2: PathAtom = PathAtom.create("".cs"");
  export const result = a1 === a2;
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(true, result.Values[0]);
        }

        [Fact]
        public void TestRelativePathHasExtension()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p1: RelativePath = RelativePath.create(""path/to/foo.cs"");
  export const r1 = p1.hasExtension;

  const p2: RelativePath = RelativePath.create(""path/to/foo"");
  export const r2 = p2.hasExtension;
}
", new[] { "M.r1", "M.r2" });

            result.ExpectNoError();
            result.ExpectValues(count: 2);
            Assert.Equal(true, result.Values[0]);
            Assert.Equal(false, result.Values[1]);
        }

        [Fact]
        public void TestRelativePathGetParent()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p1: RelativePath = RelativePath.create(""path/to/foo.cs"");
  const p2 = p1.parent;
  const p3: RelativePath = RelativePath.create(""path/to"");
  export const result1 = (p2 === p3);
  
  const p4: RelativePath = RelativePath.create("""");
  const p5 = p4.parent;
  export const result2 = (p5 === undefined);

  const p6: RelativePath = RelativePath.create(""foo"");
  const p7 = p6.parent;
  export const result3 = (p7 === p4);
}
", new[] { "M.result1", "M.result2", "M.result3" });

            result.ExpectNoError();
            result.ExpectValues(count: 3);
            Assert.Equal(result.Values[0], true);
            Assert.Equal(result.Values[1], true);
            Assert.Equal(result.Values[2], true);
        }

        [Fact]
        public void TestRelativePathHasParent()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p1: RelativePath = RelativePath.create(""path/to/foo.cs"");
  export const result1 = p1.hasParent;
  
  const p2: RelativePath = RelativePath.create("""");
  export const result2 = p2.hasParent;

  const p3: RelativePath = RelativePath.create(""foo"");
  export const result3 = p3.hasParent;
}
", new[] { "M.result1", "M.result2", "M.result3" });

            result.ExpectNoError();
            result.ExpectValues(count: 3);
            Assert.Equal(result.Values[0], true);
            Assert.Equal(result.Values[1], false);
            Assert.Equal(result.Values[2], true);
        }

        [Fact]
        public void TestRelativePathGetName()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p1: RelativePath = RelativePath.create(""path/to/foo.cs"");
  const name1 = p1.name;
  
  const name2: PathAtom = PathAtom.create(""foo.cs"");
  export const result = name1 === name2;
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], true);
        }

        [Fact]
        public void TestRelativePathGetNameWithoutExtension()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p1: RelativePath = RelativePath.create(""path/to/foo.cs"");
  export const result = PathAtom.create(""foo"") === p1.nameWithoutExtension;
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], true);
        }

        [Fact]
        public void TestRelativePathCombine()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p: RelativePath = RelativePath.create(""a/b/c"");
  export const result1 = p.combine(""foo.cs"") === RelativePath.create(""a/b/c/foo.cs"");
  export const result2 = p.combine(""d/e/foo.cs"") === RelativePath.create(""a/b/c/d/e/foo.cs"");
}
", new[] { "M.result1", "M.result2" });

            result.ExpectNoError();
            result.ExpectValues(count: 2);
            Assert.Equal(result.Values[0], true);
            Assert.Equal(result.Values[1], true);
        }

        [Fact]
        public void TestRelativePathCombinePaths()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p: RelativePath = RelativePath.create(""a/b/c"");
  const q = [""d/e"", ""f/g"", ""file.cs""];
  export const result = p.combinePaths(...q) === RelativePath.create(""a/b/c/d/e/f/g/file.cs"");
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], true);
        }

        [Fact]
        public void TestRelativePathConcat()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p: RelativePath = RelativePath.create(""a/b/c"");
  export const result = p.concat(""_extended"") === RelativePath.create(""a/b/c_extended"");
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], true);
        }

        [Fact]
        public void TestRelativePathToFromPathAtoms()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p: RelativePath = RelativePath.create(""path/to/file/x.cs"");
  const q: PathAtom[] = p.toPathAtoms();
  const r: RelativePath = RelativePath.fromPathAtoms(...q);
  export const result = p === r;
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], true);
        }

        [Fact]
        public void TestRelativePathToString()
        {
            var result = EvaluateSpec(@"
namespace M 
{
  const p: RelativePath = RelativePath.create(""path/to/file/x.cs"");
  export const result = p.toString();
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], "r`path/to/file/x.cs`");
        }
    }
}
