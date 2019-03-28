// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretInterfaces : DsTest
    {
        public InterpretInterfaces(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void InterfaceWithMethod()
        {
            string code = @"
interface SemVer {
    major: number;
    minor: number;
    patch: number;
    toSemVerString: () => string;
}


    function createSemVerFromVersion() : SemVer {
        //const semVerParts = '';//version.split('.');
        //const semVerPartsLength = semVerParts.length;
        const newSemVer : SemVer = {
            major: 1,// semVerPartsLength > 1 ? Number.parseInt(semVerParts[1]) : 0,
            minor: 2,//semVerPartsLength > 2 ? Number.parseInt(semVerParts[2]) : 0,
            patch: 3,//semVerPartsLength > 3 ? Number.parseInt(semVerParts[3]) : 0,

            toSemVerString: () => {
                return `${newSemVer.major}.${newSemVer.minor}.${newSemVer.patch}`;
            }
        };

        return newSemVer;
    }

export const r = createSemVerFromVersion().toSemVerString();
";
            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal("1.2.3", result);
        }

        [Fact]
        public void ObjectLiteralWithStringKey()
        {
            string code = @"
const lockFileTargets = {
    "".NETFramework, Version=v4.6"": [
        { assembly: ""System.Threading.Task.Dataflow"", version: ""4.5.25"", path: 'lib/dotnet/System.Threading.Tasks.Dataflow.dll'}
    ],
    "".NETFramework,Version=v4.5"": [
        { assembly: ""System.Threading.Task.Dataflow"", version: ""4.5.21"", path: 'lib/dotnet-4.5.21/System.Threading.Tasks.Dataflow.dll'}
    ],
};

export const version = lockFileTargets["".NETFramework, Version=v4.6""][0].version;";

            var result = EvaluateExpressionWithNoErrors(code, "version");
            Assert.Equal("4.5.25", result);
        }
        [Fact]
        public void ObjectLiteralWithAFunction()
        {
            string code = @"
function foo() {return 42;}
const obj = {x: 42, foo: foo};
export const r = obj.foo();
";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(42, result);
        }

        [Fact]
        public void InterpretInterfaceInstantiationOfTypeLiteral()
        {
            string code = @"
namespace M {
    const x: {n: number, s: string } = {n: 42, s: ""42""};
    export const r1 = x.n;
    export const r2 = x.s;
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(42, result["M.r1"]);
            Assert.Equal("42", result["M.r2"]);
        }

        [Fact]
        public void InterpretInterfaceDeclarationWithShorthandPropertyAssignment()
        {
            string code = @"
namespace M {
    interface X {
        n: number;
        s: string;
    }
    
    const n = 42;
    const s = ""42"";
    const x: X = {n, s};
    export const r1 = x.n;
    export const r2 = x.s;
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(42, result["M.r1"]);
            Assert.Equal("42", result["M.r2"]);
        }

        [Fact]
        public void InterpretInterfaceWithDeclaration()
        {
            string code = @"
namespace M {
    interface Point {
        x: number;
        y: number;
    }

    interface Shape {
        p: Point;
        x: {x: number; y: number};
    }

    const r: Shape = {
        p: {x: 1, y: 2},
        x: {x: 3, y: 4},
    };

    export const r1 = r.p.x; // 1
    export const r2 = r.x.y; // 4
}";

            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(1, result["M.r1"]);
            Assert.Equal(4, result["M.r2"]);
        }

        [Fact]
        public void InterpretGenericInterfaceDeclaration()
        {
            string code = @"
namespace M {
    interface Point<T> {
        x: T;
        y: T;
    }

    interface Shape<T1, T2> {
        p1: Point<T1>;
        p2: Point<T2>;
    }

    const r: Shape<number, string> = {
        p1: {x: 1, y: 2},
        p2: {x: ""3"", y: ""4""},
    };

    export const r1 = r.p1.x; // 1
    export const r2 = r.p2.y; // ""4
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(1, result["M.r1"]);
            Assert.Equal("4", result["M.r2"]);
        }

        [Fact]
        public void InterpretInterfaceWithOptionalFunctionDeclaration()
        {
            string code = @"
namespace M {
    interface X {
        n: number;
        s?: string;
        toString(): string;
        override<T>(other: Object): T;
    }

    const x: X = {n: 42, s: ""42"", toString: undefined, override: undefined};
    export const r1 = x.n;
    export const r2 = x.s;
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2");

            Assert.Equal(42, result["M.r1"]);
            Assert.Equal("42", result["M.r2"]);
        }
    }
}
