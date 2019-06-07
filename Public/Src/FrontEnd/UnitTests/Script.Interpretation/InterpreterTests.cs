// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpreterTests : DsTest
    {
        public InterpreterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void VerySimple()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const x = 42;
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(42, result.Values[0]);
        }

        [Fact]
        public void TestString()
        {
            var result = EvaluateSpec(@"
namespace M
{
    const x : string = ""abc"".concat(""def"");
    export const result = x.endsWith(""ef"");
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(true, result.Values[0]);
        }

        [Fact]
        public void TestStringUndefinedOrEmpty()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const b1 : boolean = String.isUndefinedOrEmpty(undefined);
    export const b2 : boolean = String.isUndefinedOrEmpty("""");
    export const b3 : boolean = String.isUndefinedOrEmpty(""hello"");
}
", new[] { "M.b1", "M.b2", "M.b3" });

            result.ExpectNoError();
            result.ExpectValues(count: 3);
            Assert.Equal(true, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
            Assert.Equal(false, result.Values[2]);
        }

        [Fact]
        public void TestStringUndefinedOrWhitespace()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const b1 : boolean = String.isUndefinedOrWhitespace(undefined);
    export const b2 : boolean = String.isUndefinedOrWhitespace("""");
    export const b3 : boolean = String.isUndefinedOrWhitespace(""hello"");
    export const b4 : boolean = String.isUndefinedOrWhitespace("" "");
}
", new[] { "M.b1", "M.b2", "M.b3", "M.b4" });

            result.ExpectNoError();
            result.ExpectValues(count: 4);
            Assert.Equal(true, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
            Assert.Equal(false, result.Values[2]);
            Assert.Equal(true, result.Values[3]);
        }

        [Fact]
        public void TestArraySpread()
        {
            var result = EvaluateSpec(@"
namespace M
{
    const x = [1, 2, 3];
    const y = [4, 5, 6];
    export const z = [0, ...x, 7, 8, ...y, 9];
    export const zLen = z.length;
}
", new[] { "M.zLen" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(10, result.Values[0]);
        }

        [Fact]
        public void TestLastActiveUseName()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function fun() : string { return Context.getLastActiveUseName(); }
    export const z = fun();
}
", new[] { "M.z" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal("M.z", result.Values[0]);
        }

        [Fact]
        public void TestFibo()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function fibo(n: number): number
    {
        if (n === 0) { return 0; }
        if (n === 1) { return 1; }
        return fibo(n - 1) + fibo(n - 2);
    }

    export const x = fibo(5);
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(5, result.Values[0]);
        }

        [Fact]
        public void TestEnumValue()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"MyProject/project.dsc", @"
namespace MyProject
{
    const enum Fruit
    {
        mango = 1,
        banana = 2,
        papaya = 4,
        apple = 8
    }

    export const x = Fruit.papaya.toString();
    const y = Fruit.mango | Fruit.banana | Fruit.apple;
    const w = Fruit.mango ^ Fruit.banana;
    export const z = (y & Fruit.banana) === Fruit.banana;
    export const t = (y & w) === w;
    export const m = y.hasFlag(Fruit.banana | Fruit.apple);
    export const n = ~~Fruit.banana === Fruit.banana;
}");
            var result = Evaluate(testWriter, @"MyProject/project.dsc", new[] { "MyProject.x", "MyProject.z", "MyProject.t", "MyProject.m", "MyProject.n" });

            result.ExpectNoError();
            result.ExpectValues(count: 5);
            Assert.Equal("papaya", result.Values[0]);
            Assert.Equal(true, result.Values[1]);
            Assert.Equal(true, result.Values[2]);
            Assert.Equal(true, result.Values[3]);
            Assert.Equal(true, result.Values[4]);
        }

        [Fact]
        public void TestSpreadCall()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function f(dummy: number, ...x: any[]) : number {
        return x.length;
    }

    const arr = [1, 2, 3];
    export const fArr = f(0, arr);
    export const fSpreadArr = f(0, ...arr);
}
", new[] { "M.fArr", "M.fSpreadArr" });

            result.ExpectNoError();
            result.ExpectValues(count: 2);
            Assert.Equal(1, result.Values[0]);
            Assert.Equal(3, result.Values[1]);
        }

        [Fact]
        public void TestOptionalRest()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function f(x: number, y?: number, ...z: number[]) {
        return { y: y, zLen: z.length };
    }

    export const r0 = f(0, 1, 2, 3);
    export const r1 = f(0, 1);
    export const r2 = f(0);

    export const r0y = r0.y;
    export const r0zLen = r0.zLen;
    export const r1y = r1.y;
    export const r1zLen = r1.zLen;
    export const r2y = r2.y;
    export const r2zLen = r2.zLen;
}
", new[] { "M.r0y", "M.r0zLen", "M.r1y", "M.r1zLen", "M.r2y", "M.r2zLen" });

            result.ExpectNoError();
            result.ExpectValues(count: 6);

            Assert.Equal(1, result.Values[0]);
            Assert.Equal(2, result.Values[1]);
            Assert.Equal(1, result.Values[2]);
            Assert.Equal(0, result.Values[3]);
            Assert.Equal(UndefinedValue.Instance, result.Values[4]);
            Assert.Equal(0, result.Values[5]);
        }

        // TODO: please split those test cases after moving them to new parser.
        [Fact]
        public void TestErrorValue()
        {
            var testWriter = CreateTestWriter();
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"project.dsc", @"
function f(y : {i: {x: string}})
{
    const q = 1 + (<number><any>y['i']);
}

export const z = f({i: {x: ""def""}});

const p1 = p`path/abc.txt`;
export const pp = p1.changeExtension(<string><any>0);

function myjoin(a : string[])
{
    return a.join(""; "");
}

export const a = myjoin(<string[]>[""abc"", ""def"", 1, ""ghi""]);

export const b = (() => 1 + undefined)();
");

            var result = Evaluate(testWriter, @"project.dsc", new[] { "z", "pp", "a", "b" });

            result.ExpectValues(count: 4);

            XAssert.AreEqual(ErrorValue.Instance, result.Values[0]);
            XAssert.AreEqual(ErrorValue.Instance, result.Values[1]);
            XAssert.AreEqual(ErrorValue.Instance, result.Values[2]);
            XAssert.AreEqual(ErrorValue.Instance, result.Values[3]);
            result.ExpectErrorCode((int)LogEventId.UnexpectedValueType, count: 2);
            result.ExpectErrorCode((int)LogEventId.UnexpectedValueTypeOnConversion, count: 2);
            result.ExpectErrorMessageSubstrings(
                new[]
                {
                    "'string or PathAtom' for argument 1, but got '0' of type 'number'",
                });
        }

        [Fact]
        public void TestDivideByZero()
        {
            var result = EvaluateSpec(@"
namespace M
{
    const x : number = 42;
    const y : number = 0;
    export const result : number = x % y;
}
", new[] { "M.result" });

            result.ExpectValues(count: 1);
            Assert.Equal(ErrorValue.Instance, result.Values[0]);
            result.ExpectErrorCode((int)LogEventId.DivideByZero, count: 1);
        }

        [Fact]
        public void TestStackOverflow()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function f(n: number) : number {
        if (n === 0) return n;
        return f(n - 1);
    }

    export const result0 : number = f(10000);
    export const result1 : number = f(42);
}
", new[] { "M.result0", "M.result1" });

            result.ExpectValues(count: 2);
            Assert.Equal(ErrorValue.Instance, result.Values[0]);
            Assert.Equal(0, result.Values[1]);
            result.ExpectErrorCode((int)LogEventId.StackOverflow, count: 1);
        }

        [Fact(Skip = "Moved to separate test cases")]
        public void TestUndefinedReferences()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const dir = Context.getNewOutputDirectory(undefined);

    const sub: string[] = undefined;
    export const array = [...sub, ""foo""];

    const q: Path = undefined;
    export const path = q.changeExtension(""bar"");
}
", new[] { "M.dir", "M.array", "M.path" });

            result.ExpectErrors(count: 3);
            result.ExpectValues(count: 3);
            Assert.Equal(ErrorValue.Instance, result.Values[0]);
            Assert.Equal(ErrorValue.Instance, result.Values[1]);
            Assert.Equal(ErrorValue.Instance, result.Values[2]);

            result.ExpectErrorCode((int)LogEventId.UnexpectedValueTypeOnConversion, count: 2);
            result.ExpectErrorMessageSubstrings(
                new[]
                {
                    "'changeExtension' of 'q' cannot be resolved",
                    "'Array' for argument 1, but got 'undefined'",
                    "'string | PathAtom' for argument 1, but got 'undefined'"
                });
        }

        [Fact]
        public void TestMissingAmbientFunctionOrField()
        {
            const string spec = @"
export namespace M
{
     export const result = PathAtom.missingFunction();
}
";
            var result = Build()
                .AddExtraPreludeSpec(@"
/// <reference path=""Prelude.Core.dsc""/>
namespace PathAtom {
    export declare function missingFunction(): any;
}
")
                .AddSpec(MainSpecRelativePath, spec)
                .Evaluate("M.result");

            result.ExpectErrors(count: 1);
            result.ExpectValues(count: 1);
            Assert.Equal(ErrorValue.Instance, result.Values[0]);
            result.ExpectErrorCode((int)LogEventId.MissingNamespaceMember, count: 1);
            result.ExpectErrorMessageSubstrings(
                new[]
                {
                    "Missing field or member 'missingFunction'"
                });
        }

        [Fact]
        public void TestCastToInterface()
        {
            var result = EvaluateSpec(@"
namespace M
{
  interface Pair<K, V> {
    key: K;
    value: V;
  }

  export const p1 = <Pair<string, number>>{
    key: ""asdf"",
    value: 234
  };

  export const p2 = {
    key: ""asdf"",
    value: 234
  } as Pair<string, number>;

  export const p3 = <Pair<string, number>>p1;
  export const p4 = p2 as Pair<string, number>;
}
", new[] { "M.p1", "M.p2", "M.p3", "M.p4" });

            result.ExpectNoError();
            result.ExpectValues(count: 4);

            Action<object> checkObjFun = obj =>
                                         {
                                             var objLit = obj as ObjectLiteral;
                                             Assert.NotNull(objLit);
                                             Contract.Assume(objLit != null);
                                             Assert.Equal(2, objLit.Count);
                                             Assert.Equal("asdf", objLit[CreateString("key")].Value);
                                             Assert.Equal(234, objLit[CreateString("value")].Value);
                                         };

            for (int i = 0; i < 4; i++)
            {
                checkObjFun(result.Values[i]);
            }
        }

        [Fact]
        public void TestCastToInterfaceNoTypeChecking()
        {
            var result = EvaluateSpec(@"
namespace M
{
  interface Pair<K, V> {
    key: K;
    value: V;
  }

  export const p1 = <Pair<string, number>>{};
  export const p2 = {} as Pair<string, number>;
  export const p3 = <Pair<string, number>>p1;
  export const p4 = p2 as Pair<string, number>;
}
", new[] { "M.p1", "M.p2", "M.p3", "M.p4" });

            result.ExpectNoError();
            result.ExpectValues(count: 4);

            Action<object> checkObjFun = obj => { Assert.NotNull(obj as ObjectLiteral0); };

            for (int i = 0; i < 4; i++)
            {
                checkObjFun(result.Values[i]);
            }
        }

        [Fact]
        public void TestEnvVarQuery()
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                var unixEnv = EvaluateSpec(@"
                namespace M
                {
                    export const homePath = Environment.getStringValue(""HOME"");
                }", new[] { "M.homePath" });

                unixEnv.ExpectNoError();
                unixEnv.ExpectValues(count: 1);

                return;
            }

            var result = EvaluateSpec(@"
                namespace M
                {
                    export const userName = Environment.getStringValue(""USERNAME"");
                    export const systemRoot = Environment.getPathValue(""SystemRoot"").toString();
                    const pvs = Environment.getPathValues(""Path"", "";"");
                    export const pathValues = pvs.reduce((accum, path) => { return accum + path.toString() + "";""; } , """");
                }", new[] { "M.userName", "M.systemRoot", "M.pathValues" });

            result.ExpectNoError();
            result.ExpectValues(count: 3);

            string separator = ";";
            Func<string, string> unNormalizePath =
                path =>
                {
                    Contract.Requires(!string.IsNullOrWhiteSpace(path));
                    return path.Replace("/", "\\").Replace("\\;", separator).Replace("p`", string.Empty).Replace("`", string.Empty);
                };

            Func<string, string> toUpperInvariant = s => string.IsNullOrWhiteSpace(s) ? s : s.ToUpperInvariant();

            var userName = Environment.GetEnvironmentVariable("USERNAME");
            var sysRoot = Environment.GetEnvironmentVariable("SystemRoot");
            var paths = Environment.GetEnvironmentVariable("Path");
            var pathValues = paths.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            var absolutePaths = pathValues.Aggregate((currentAccumPaths, nextPath) => currentAccumPaths + separator + Path.GetFullPath(nextPath));

            if (userName != null)
            {
                Assert.Equal(userName, result.Values[0]);
            }
            else
            {
                Assert.Equal(UndefinedValue.Instance, result.Values[0]);
            }

            if (sysRoot != null)
            {
                Assert.Equal(
                    toUpperInvariant(unNormalizePath(sysRoot)),
                    toUpperInvariant(unNormalizePath(result.Values[1] as string)));
            }
            else
            {
                Assert.Equal(UndefinedValue.Instance, result.Values[1]);
            }

            if (paths != null)
            {
                Assert.Equal(
                    toUpperInvariant(unNormalizePath(absolutePaths)) + ";",
                    toUpperInvariant(unNormalizePath(result.Values[2] as string)));
            }
            else
            {
                Assert.Equal(UndefinedValue.Instance, result.Values[2]);
            }
        }

        [Fact]
        public void TestNestedLetIf()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function fun() {
        const x: number = 0;
        let y: number = 0;

        if (x === 0) {
            const x: number = 1;
            if (x === 1) {
                y = 42;
            }
        }

        return x + y;
    }

    export const result = fun();
}", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(42, result.Values[0]);
        }

        [Fact]
        public void TestNestedLetFor()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function fun() {
        const x: string = ""A"";
        const xs: string[] = [""B"", ""C""];
        let y: string = """";

        for (let x of xs) {
            y = y + x;
        }

        return x + y;
    }

    export const result = fun();
}", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal("ABC", result.Values[0]);
        }

        [Fact]
        public void TestNestedLetLambdaCapture()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function fun(a: string) {
        let b: string = ""let(b);"";
        let f = undefined;
        {
            let b: string = ""let_nested(b);"";
            f = (p, q) => { return p + b + q; };
            b = ""let_nested_modified(b);"";
        }

        b = ""let_end(b);"";
        return f(a, b);
    }

    export const result = fun(""result: "");
}", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal("result: let_nested_modified(b);let_end(b);", result.Values[0]);
        }

        [Fact]
        public void TestEvaluateSingleProject()
        {
            CreateBuildExtentTestBuilder()
                .RootSpec(Path.Combine("MyPackage", "MySpec.dsc"))
                .EvaluateWithDiagnosticId(LogEventId.ContractAssert);
        }

        [Fact]
        public void TestEvaluatePackageShouldFail()
        {
            CreateBuildExtentTestBuilder()
                .RootSpec(Path.Combine("MyPackage", "MyOtherSpec.dsc"))
                .EvaluateWithDiagnosticId(LogEventId.ContractAssert);
        }

        [Fact]
        public void TestEvaluateConfigShouldFail()
        {
            var specBuilder = CreateBuildExtentTestBuilder();
            var result = specBuilder.RootSpec("config.dsc").EvaluateWithFirstError();
            Assert.Equal((int) LogEventId.ContractAssert, result.ErrorCode);
        }

        private SpecEvaluationBuilder CreateBuildExtentTestBuilder()
        {
            var specBuilder = Build();

            specBuilder.LegacyConfiguration("config({});");

            specBuilder.AddFile(
                Path.Combine("MyPackage", global::BuildXL.FrontEnd.Script.Constants.Names.ModuleConfigDsc),
                @"
module({
    name: ""MyPackage"",
});");

            specBuilder.AddSpec(
                Path.Combine("MyPackage", "MySpec.dsc"),
                @"
export const otherX = x;");

            specBuilder.AddSpec(
                Path.Combine("MyPackage", "MyOtherSpec.dsc"),
                @"
export const x = ""OK"";
const y = Contract.assert(false, ""Not OK"");");

            return specBuilder;
        }
       
        [Fact]
        public void TestTypeOf()
        {
            var result = EvaluateExpressionsWithNoErrors(@"
namespace M {
    export function f() { return 0; }
    export const enum En { en1, en2 }
}

export const undef = typeof undefined;
export const path = typeof p`a/b/c`;
export const atom = typeof PathAtom.create(""atom"");
export const str = typeof ""foo"";
export const num = typeof 7;
export const bool = typeof !!true;
export const obj = typeof { a: 1, b: 2};
export const arr = typeof [1, 2, 3];
export const fun = typeof M.f;
export const map = typeof Map.empty<string, number>();
export const map2 = typeof Map.emptyCaseInsensitive<number>();
export const set = typeof Set.empty<number>();
export const en = typeof M.En.en1;
export const mod = typeof M;

", new[] { "undef", "path", "atom", "str", "num", "bool", "obj", "arr", "fun", "map", "map2", "set", "en", "mod" });

            Assert.Equal("undefined", result["undef"]);
            Assert.Equal("Path", result["path"]);
            Assert.Equal("PathAtom", result["atom"]);
            Assert.Equal("string", result["str"]);
            Assert.Equal("number", result["num"]);
            Assert.Equal("boolean", result["bool"]);
            Assert.Equal("object", result["obj"]);
            Assert.Equal("array", result["arr"]);
            Assert.Equal("function", result["fun"]);
            Assert.Equal("Map", result["map"]);
            Assert.Equal("Map", result["map2"]);
            Assert.Equal("Set", result["set"]);
            Assert.Equal("enum", result["en"]);
            Assert.Equal("object", result["mod"]);
        }

        [Fact]
        public void TestRequirePackage()
        {
            var testWriter = CreateTestWriter("src");
            var configWriter = testWriter.ConfigWriter;

            configWriter.AddBuildSpec(@"project.dsc", @"
const z = importFrom(""Coco"").Coco;
export const y = z.x;");

            testWriter.AddExtraFile(@"../Externals/Coco/package.dsc", @"
namespace Coco {
    export const x: number = 42 + 0;
}");
            testWriter.AddExtraFile(@"../Externals/Coco/package.config.dsc", @"
module({
    name: ""Coco""
});");

            var sourceResolver = configWriter.AddSourceResolver();
            sourceResolver.AddPackage(@"../Externals/Coco");

            configWriter.AddDefaultSourceResolver();

            var result = Evaluate(testWriter, @"project.dsc", new[] { "y" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(42, result.Values[0]);
        }

        #region Bugs

        [Fact(Skip = "Handle error cases and re-enable")]
        public void Bug529326()
        {
            string code = @"export const x = [
  {name: """", value: 1},
  ...(1.any_code_that_throws_here)
];";

            var result = EvaluateSpec(code, new string[0]);

            // var diagnostic = ParseWithFirstFailure(code);
            // Assert.Equal(ParseErrors.Code.TypeScriptSyntaxError, diagnostic.Code);

            result.ExpectErrors(count: 1);

            // Coco - based and TypeScript-based parses has different errors here.
            //  Old parser says that 'Selector 'any_code_that_throws_here' of '1' cannot be resolved'
            // and new one says: expected ','
            result.ExpectErrorMessageSubstrings(new[] { "Selector 'any_code_that_throws_here' of '1' cannot be resolved" });
        }

        [Fact(Skip = "Ast conversion will catch this now and won't allow any")]
        public void Bug545937()
        {
            var result = EvaluateSpec(@"
namespace Y {
  function f() { return []; }
  export const result = (<any>f).push(1);
}", new string[0]);

            result.ExpectErrors(count: 1);
            result.ExpectErrorCode((int)LogEventId.UnexpectedValueType, count: 1);
            result.ExpectErrorMessageSubstrings(new[] { "Expecting '<any>f' of type(s) 'Object, Array or Module', but got '<function:f()>' of type 'function'" });
        }

        #endregion Bugs
    }
}
