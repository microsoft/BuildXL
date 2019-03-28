// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using TypeScript.Net.Diagnostics;
using Xunit;

namespace TypeScript.Net.UnitTests.TypeChecking
{
    // Mainly used for debuging issues.
    public sealed class StringBasedCheckerTests
    {
        [Fact]
        public void ErrorMessageWithPlusOperatorOnNumberAndFunctionShouldBeCorrect()
        {
            string code =
@"let b: number = 42;
function foo() { }
var r15 = b + foo;";

            var diagnostics = GetSemanticDiagnostics(code);
            Assert.NotEmpty(diagnostics);

            var error = diagnostics.First();

            // There was a bug that functions were printed incorrectly.
            // This test checks that case.
            Assert.Contains("() =>", error.MessageText.ToString());
        }

        [Fact]
        public void TestDupliateDefaultsWithUnicodeEscapeCharacters()
        {
            string code =
@"
var x = 10;

switch (x) {
    case 1:
    case 2:
    default:    // No issues.
        break;
    default:    // Error; second 'default' clause.
    default:    // Error; third 'default' clause.
    case 3:
        x *= x;
}

switch (x) {
    default:    // No issues.
        break;
    case 100:
        switch (x * x) {
            default:    // No issues.
            default:    // Error; second 'default' clause.
                break;
            case 10000:
                x /= x;
            default:    // Error, third 'default' clause
            def\u0061ult: // Error, fourth 'default' clause.
            // Errors on fifth-seventh
            default: return;
            default: default:
        }
}";

            var diagnostics = GetSemanticDiagnostics(code);
            Assert.NotEmpty(diagnostics);

            var error = diagnostics.First();

            Assert.Contains("A 'default' clause cannot appear more than once in a", error.MessageText.ToString());
        }

        [Fact]
        public void ArrayBindingPatternOmittedExpressions()
        {
            string code =
@"var results: string[];

{
    let [, b, , a] = results;
    let x = {
        a,
        b
    }
}


function f([, a, , b, , , , s, , , ] = results) {
    a = s[1];
    b = s[2];
}
";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact]
        public void StringLiterals()
        {
            string code = @"
export type BuildPass = 'prePass' | 'passIndependent' | 'pass0' | 'pass1' | 'pass2' | 'pass3';

export interface Arguments {
    /** Specific pass to execute. */
    targetPass?: BuildPass;
}

export function build(inputArgs: Arguments): any {
}

const isBuildCommonArchitecture: boolean = true;

const prePass = isBuildCommonArchitecture
    ? build({
        targetPass: 'pass3',
    })
    : undefined; 
";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact(Skip = "Need fo fix. Important feature!")]
        public void ObjectLiteralExcessProperties()
        {
            string code =
@"interface Book {
    foreword: string;
}

interface Cover {
    color?: string;
}

var b1: Book = { forword: ""oops"" };

var b2: Book | string = { foreward: ""nope"" };

var b3: Book | (Book[]) = [{ foreword: ""hello"" }, { forwards: ""back"" }];

var b4: Book & Cover = { foreword: ""hi"", colour: ""blue"" };

var b5: Book & Cover = { foreward: ""hi"", color: ""blue"" };

var b6: Book & Cover = { foreword: ""hi"", color: ""blue"", price: 10.99 };

var b7: Book & number = { foreword: ""hi"", price: 10.99 };";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact(Skip = "Need to fix")]
        public void EnumIdentifierLiterals()
        {
            string code =
@"enum Nums {
    1.0,
    11e-1,
    0.12e1,
    ""13e-1"",
    0xF00D
}";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact]
        public void ForwardRefInEnum()
        {
            string code =
@"enum E1 {
    // illegal case
    // forward reference to the element of the same enum
    X = Y, 
    X1 = E1[""Y""], 
    // forward reference to the element of the same enum
    Y = E1.Z,
    Y1 = E1[""Z""]
}

enum E1
{
    Z = 4
}
";
            var diagnostics = GetSemanticDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }

        [Fact(Skip = "Need to fix")]
        public void EnumInitializerWithExponents()
        {
            string code =
@"// Must be integer literals.
declare enum E {
    a = 1e3, // ok
    b = 1e25, // ok
    c = 1e-3, // error
    d = 1e-9, // error
    e = 1e0, // ok
    f = 1e+25 // ok
}";
            var diagnostics = GetSemanticDiagnostics(code);
            Assert.Equal(3, diagnostics.Count);
        }

        [Fact]
        public void ConstIndexedAccess()
        {
            string code =
@"
const enum numbers {
    zero,
    one
}

interface indexAccess {
    0: string;
    1: number;
}

let test: indexAccess;

let s = test[0];
let n = test[1];

let s1 = test[numbers.zero];
let n1 = test[numbers.one];

let s2 = test[numbers[""zero""]];
let n2 = test[numbers[""one""]];

enum numbersNotConst
{
    zero,
    one
}

let s3 = test[numbersNotConst.zero];
let n3 = test[numbersNotConst.one];";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact]
        public void BlockScopedBindingsReassignmentInLoop()
        {
            string code =
@"function f1() {
    for (let [x, y] = [1, 2]; x < y; ++x, --y) {
        let a = () => x++ + y++;
        if (x == 1)
            break;
        else if (y == 2)
            y = 5;
        else
            return;
    }
}

function f2() {
    for (let [{a: x, b: {c: y}}] = [{a: 1, b: {c: 2}}]; x < y; ++x, --y) {
        let a = () => x++ + y++;
        if (x == 1)
            break;
        else if (y == 2)
            y = 5;
        else
            return;
    }
}";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact(Skip = "Still failing!")]
        public void FunctionDeclarationWithResolutionOfTypeOfSameName()
        {
            string code =
@"interface f {
}

function f() {
    <f>f;
}";
            var diagnostics = GetSemanticDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }

        [Fact(Skip = "Still failing. Most likely related to ArrayArgument failure")]
        public void AssignmentStricterConstraints()
        {
            string code =
@"var f = function <T, S extends T>(x: T, y: S): void {
    x = y
}

var g = function <T, S>(x: T, y: S): void { }

g = f
g(1, "")";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact]
        public void Indexer()
        {
            string code =
@"interface JQueryElement {
    id:string;
}

interface JQuery {
    [n:number]:JQueryElement;
}

var jq:JQuery={ 0: { id : ""a"" }, 1: { id : ""b"" } };
jq[0].id; ";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact(Skip = "Needs to be fixed ASAP!")]
        public void Literals()
        {
            string code =
@"var a = 42;
var b = 0xFA34;
var c = 0.1715;
var d = 3.14E5;
var e = 8.14e-5;

var f = true;
var g = false;

var h = "";
var i = ""hi"";
var j = '';
var k = 'q\tq';

var m = / q /;
var n = /\d +/ g;
var o = /[3 - 5] +/ i; ";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact]
        public void IsArray()
        {
            string code =
@"var maybeArray: number | number[];

if (Array.isArray(maybeArray)) {
    maybeArray.length; // OK
}
else {
    maybeArray.toFixed(); // OK
}";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact(Skip = "Still failing. But this issue could be complicated to solve!")]
        public void CheckAnalysisOrderIssue()
        {
            // Currently we have a problem with reusing AST.
            // For instance, trying to reuse parsed library lead to failure
            // if we're trying to check code1 and then code2, but we can
            // easily check code 2 and after that code1 will work.

            // AST is mutable and checker can assign values from its internal table
            // for nodes that would be missing during subsequent analysis.
            // This is actually a real problem, because parsing lib.d.ts file each time takes
            // significant amount of time.
            string code1 =
@"var results: string[];

{
    let [, b, , a] = results;
    let x = {
        a,
        b
    }
}

function f([, a, , b, , , , s, , , ] = results) {
    a = s[1];
    b = s[2];
}
";

            string code2 =
@"var maybeArray: number | number[];

if (Array.isArray(maybeArray)) {
    maybeArray.length; // OK
}
else {
    maybeArray.toFixed(); // OK
}";
            var diagnostics = GetSemanticDiagnostics(code1);
            ExpectNoErrors(diagnostics);

            diagnostics = GetSemanticDiagnostics(code2);
            ExpectNoErrors(diagnostics);
        }

        [Fact(Skip = "Crashes right now!")]
        public void LocalVariablesReturnedFromCatchBlocks()
        {
            string code =
@"function f() {
    try {
    } catch (e) {
        var stack2 = e.stack;
        return stack2; //error TS2095: Could not find symbol 'stack2'.
    }
}";
            var diagnostics = GetSemanticDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }

        [Fact(Skip = "Still failing!")]
        public void ArrayAugment()
        {
            string code =
@"interface Array<T> {
    split: (parts: number) => T[][];
}

var x = [''];
var y = x.split(4);
var y: string[][]; // Expect no error here
";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact]
        public void CheckBindingPatternInParameter()
        {
            string code =
@"const nestedArray = [[[1, 2]], [[3, 4]]];

nestedArray.forEach(([[a, b]]) => {
  log(a, b);
});

function log(a: any, b: any) {}
";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact]
        public void StaticInAFunction()
        {
            string code =
@"function boo{
   static test()
   static test(name:string)
   static test(name?:any){}
}";
            var diagnostics = GetSemanticDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }

        [Fact]
        public void CheckRecursiveInheritanceWithItself()
        {
            string code =
@"interface I5 extends I5 { // error
    foo():void;
}";
            var diagnostics = GetSemanticDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }

        [Fact(Skip = "Need to fix it! Failes with stack overflow!")]
        public void CheckRecursiveInheritance()
        {
            string code =
@"interface i8 extends i9 { } // error
interface i9 extends i8 { } // error
";
            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        [Fact]
        public void CheckArrayAssignability()
        {
            string code =
@"var x: string[];
x = new Array(1);
x = new Array('hi', 'bye');
x = new Array<string>('hi', 'bye');

var y: number[];
y = new Array(1);
y = new Array(1,2);
y = new Array<number>(1, 2);
";

            var diagnostics = GetSemanticDiagnostics(code);
            ExpectNoErrors(diagnostics);
        }

        private static void ExpectNoErrors(ICollection<Diagnostic> diagnostics)
        {
            if (diagnostics.Count != 0)
            {
                string message = $"Expected no errors but got {diagnostics.Count}.\r\n{DiagnosticMessages(diagnostics)}";
                CustomAssert.Fail(message);
            }
        }

        private static string DiagnosticMessages(IEnumerable<Diagnostic> diagnostics)
        {
            return string.Join(Environment.NewLine, diagnostics.Select(d => d.MessageText.ToString()));
        }

        private static List<Diagnostic> GetSemanticDiagnostics(string code)
        {
            return TypeCheckingHelper.GetSemanticDiagnostics(useCachedVersion: false, codes: code);
        }
    }
}
