// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientStaticDirectoryTests : DsTest
    {
        public AmbientStaticDirectoryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestGetFileFailsWhenFileIsNotFound()
        {
            string spec = @"
import {Transformer} from 'Sdk.Transformers';

const x = Transformer.sealDirectory(d`.`, globR(d`.`, ""*""));
const file1 = x.getFile(p`unknown`); // should fail!
";
            var result = EvaluateWithFirstError(spec);
            Assert.Equal(LogEventId.FileNotFoundInStaticDirectory, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void TestGetFilesFailsWhenFileIsNotFound()
        {
            string spec = @"
import {Transformer} from 'Sdk.Transformers';

const x = Transformer.sealDirectory(d`.`, globR(d`.`, ""*""));
const file1 = x.getFiles([p`unknown`]); // should fail!
";
            var result = EvaluateWithFirstError(spec);
            Assert.Equal(LogEventId.FileNotFoundInStaticDirectory, (LogEventId)result.ErrorCode);
        }

        [Fact]
        public void TestStaticDirectoryRoot()
        {
            var result = EvaluateSpec(@"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const x: StaticDirectory = Transformer.sealDirectory(d`.`, glob(d`.`, ""*""));
    const root = x.root;

    export const result1 = root.path === p`.`;

}", new[] {"M.result1"});

            result.ExpectNoError();
            result.ExpectValues(count: 1);

            Assert.Equal(result.Values[0], true);
        }

        [Fact]
        public void TestStaticDirectoryGetFiles()
        {

            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const x = Transformer.sealDirectory(d`.`, globR(d`.`, ""*""));

    const expectedA = [f`dirs/a.txt`, f`dirs/b.txt`];
    const resultA = x.getFiles([r`dirs/a.txt`, r`dirs/b.txt`]);

    const expectedB = [f`dirs/b.txt`, f`dirs/a.txt`];
    const resultB = x.getFiles([r`dirs/b.txt`, r`dirs/a.txt`]);

    export const result0_a = resultA.length === expectedA.length;
    export const result0_b = resultA[0] === expectedA[0];
    export const result0_c = resultA[1] === expectedA[1];

    export const result1_a = resultB.length === expectedB.length;
    export const result1_b = resultB[0] === expectedB[0];
    export const result1_c = resultB[1] === expectedB[1];
}");

            testWriter.AddExtraFile(@"MyProject/dirs/a.txt", "Awesome");
            testWriter.AddExtraFile(@"MyProject/dirs/b.txt", "Super");

            var resultIds = new[] { "M.result0_a", "M.result0_b", "M.result0_c", "M.result1_a", "M.result1_b", "M.result1_c" };
            var result = Evaluate(testWriter, @"MyProject/project.dsc", resultIds);

            result.ExpectNoError();
            result.ExpectValues(count: resultIds.Length);

            for (int i = 0; i < resultIds.Length; i++)
            {
                Assert.Equal(true, result.Values[i]);
            }
        }

        [Fact]
        public void TestStaticDirectoryGetFile()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const x = Transformer.sealDirectory(d`.`, globR(d`.`, ""*""));
    const y = p`dirs/a.txt`;
    const z = RelativePath.create(""dirs/a.txt"");
    const w = x.root.getRelative(y);

    export const result0 = (y === x.getFile(y).path);
    export const result1 = (y === x.getFile(""dirs/a.txt"").path);
    export const result2 = (y === x.getFile(z).path);
    export const result3 = (y === x.getFile(w).path);
}");

            testWriter.AddExtraFile(@"MyProject/dirs/a.txt", "Awesome");
            testWriter.AddExtraFile(@"MyProject/dirs/b.txt", "Super");

            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] {"M.result0", "M.result1", "M.result2", "M.result3"});

            result.ExpectNoError();
            result.ExpectValues(count: 4);

            Assert.Equal(true, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
            Assert.Equal(true, result.Values[2]);
            Assert.Equal(true, result.Values[3]);
        }

        [Fact]
        public void TestStaticDirectoryHasFile()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const x = Transformer.sealDirectory(d`.`, globR(d`.`, ""*""));
    const y = p`dirs/a.txt`;
    const z = RelativePath.create(""dirs/a.txt"");
    const w = x.root.getRelative(y);

    export const result0 = x.hasFile(y);
    export const result1 = x.hasFile(""dirs/a.txt"");
    export const result2 = x.hasFile(z);
    export const result3 = x.hasFile(w);
    export const result4 = x.hasFile(r`nonExistent.txt`);
}");

            testWriter.AddExtraFile(@"MyProject/dirs/a.txt", "Awesome");
            testWriter.AddExtraFile(@"MyProject/dirs/b.txt", "Super");

            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] { "M.result0", "M.result1", "M.result2", "M.result3", "M.result4" });

            result.ExpectNoError();
            result.ExpectValues(count: 5);

            Assert.Equal(true, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
            Assert.Equal(true, result.Values[2]);
            Assert.Equal(true, result.Values[3]);
            Assert.Equal(false, result.Values[4]);
        }

        [Fact(Skip = "Check why this is failing")]
        public void TestStaticDirectoryGetContent()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const x = Transformer.sealDirectory(p`dirs`, glob(p`dirs`, ""*""));

    const a = p`dirs/a.txt`;
    const b = p`dirs/b.txt`;

    export const result = (x.getContent().length === 2) && (a === x.getContent()[0].path) && (b === x.getContent()[1].path);
}");

            testWriter.AddExtraFile(@"MyProject/dirs/a.txt", "Awesome");
            testWriter.AddExtraFile(@"MyProject/dirs/b.txt", "Super");

            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] {"M.result"});

            result.ExpectNoError();
            result.ExpectValues(count: 1);

            Assert.Equal(true, result.Values[0]);
        }

        [Fact]
        public void TestStaticDirectoryGetParent()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const d1: StaticDirectory = Transformer.sealDirectory(d`a/b/c`, [f`a/b/c/foo.cs`]);
    const p2 = d1.parent;
    const p3: Path = p`a/b`;
    export const r1 = (p2 === p3);
}");
            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] {"M.r1"});

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], true);
        }

        [Fact]
        public void TestStaticDirectoryHasParent()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M {
    const d1: StaticDirectory = Transformer.sealDirectory(d`a/b`, [f`a/b/foo.cs`]);
    export const r1 = d1.hasParent;
}");
            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] {"M.r1"});

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], true);
        }

        [Fact]
        public void TestStaticDirectoryGetName()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M
{
    const d1: StaticDirectory = Transformer.sealDirectory(d`a/b/bar`, [f`a/b/bar/foo.cs`]);
    const name1 = d1.name;

    const name2: PathAtom = PathAtom.create(""bar"");
    export const result = name1 === name2;
}");
            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] {"M.result"});

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], true);
        }

        [Fact]
        public void TestStaticDirectoryIsWithin()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M
{
    const d1: StaticDirectory = Transformer.sealDirectory(d`a/b/c`, [f`a/b/c/foo.cs`]);
    const p1: Path = p`a/b`;

    export const r1 = d1.isWithin(p1);
    const p2: Path = p`a/c`;
    export const r2 = d1.isWithin(p2);
}");
            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] {"M.r1", "M.r2"});

            result.ExpectNoError();
            result.ExpectValues(count: 2);
            Assert.Equal(result.Values[0], true);
            Assert.Equal(result.Values[1], false);
        }

        [Fact]
        public void TestStaticDirectoryGetRelative()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M
{
    const d1: StaticDirectory = Transformer.sealDirectory(d`a/b/c`, [f`a/b/c/foo.cs`]);
    const relative1: Path = p`a/b/c/d/e`;
    const relative2: Path = p`an/unrelated/path`;

    const d1Relative1 = d1.getRelative(relative1);
    export const r1 = d1Relative1 === RelativePath.create(""d/e"");
    export const r2 = d1Relative1.toPathAtoms()[1] === PathAtom.create(""e"");
    export const r3 = d1.getRelative(relative2) === undefined;
}");
            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] {"M.r1", "M.r2", "M.r3"});

            result.ExpectNoError();
            result.ExpectValues(count: 3);
            Assert.Equal(result.Values[0], true);
            Assert.Equal(result.Values[1], true);
            Assert.Equal(result.Values[2], true);
        }

        [Fact]
        public void TestStaticDirectoryGetPath()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
import {Transformer} from 'Sdk.Transformers';

namespace M
{
    const d1: StaticDirectory = Transformer.sealDirectory(d`a/b/c`, [f`a/b/c/foo.cs`]);
    const p1: Path = p`a/b/c`;

    export const r1 = d1.path === p1;
}
");
            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] {"M.r1"});

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(result.Values[0], true);
        }
    }
}
