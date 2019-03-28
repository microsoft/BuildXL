// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Analyzer;
using BuildXL.FrontEnd.Script.Analyzer.Utilities;
using TypeScript.Net;
using TypeScript.Net.Binding;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using Xunit;
using ScriptAnalyzer = BuildXL.FrontEnd.Script.Analyzer.Analyzer;

namespace Test.Tool.DScript.Analyzer
{
    public class PrettyPrintTests
        : AnalyzerTest<FakePrettyPrintAnalyzer>
    {
        [Fact]
        public void FunctionDeclWithManyArguments()
        {
            TestSuccess(
@"export function deployAssembly(
    assembly: string,
    targetFolder: number,
    reportDuplicate: string,
    currentResult: number,
    deploymentOptions: string,
    extra: string
) : File {
    return undefined;
}");
        }

        [Fact]
        public void FunctionDeclWithFewArguments()
        {
            TestSuccess(
@"export function deployAssembly(a: string, b: number) : File {
    return undefined;
}");
        }

        [Fact]
        public void Bug1033268_NumberInPath()
        {
            var content = @"export namespace Onecore.Enduser.Windowsupdate.Local.Test.Client.Testdata.Scancabs {

    const o = d`${objRoot}\onecore\enduser\windowsupdate\local\test\client\testdata\scancabs\${buildAlt}`;

    export const pass0 = LegacySdk.build({
            consumes: [
                f`content\downloadtest\1461e379-0835-4683-b852-438ab9290577.xml`,
            ],
            produces: [],
        });
}
".ToCharArray();

            var parser = new Parser();
            var sourceFile = parser.ParseSourceFileContent(
                "fake.dsc",
                TextSource.FromCharArray(content, content.Length),
                new TypeScript.Net.DScript.ParsingOptions(
                    namespacesAreAutomaticallyExported: true,
                    generateWithQualifierFunctionForEveryNamespace: false,
                    preserveTrivia: true,
                    allowBackslashesInPathInterpolation: true,
                    useSpecPublicFacadeAndAstWhenAvailable: false,
                    escapeIdentifiers: true,
                    convertPathLikeLiteralsAtParseTime: false));

            Assert.Equal(0, sourceFile.ParseDiagnostics.Count);

            var binder = new Binder();
            binder.BindSourceFile(sourceFile, new CompilerOptions());

            Assert.Equal(0, sourceFile.BindDiagnostics.Count);

            var scriptWriter = new ScriptWriter();
            var prettyPrintVisitor = new DScriptPrettyPrintVisitor(scriptWriter, attemptToPreserveNewlinesForListMembers: false);
            sourceFile.Cast<IVisitableNode>().Accept(prettyPrintVisitor);
        }

        [Fact]
        public void FunctionDeclWithNamedArguments()
        {
            TestSuccess(
@"interface Arguments {}
export function execute(args: Arguments) : File[] {
    return [];
}");
        }

        [Fact]
        public void FunctionGenericWithNoArgsAndVoid()
        {
            TestSuccess(
@"export function execute<T>() : void {
}");
        }

        [Fact]
        public void ForBlock()
        {
            TestSuccess(
@"function f() {
    const xs: number[] = [];
    for (var x of xs) {
        x++;
    }
}");
        }

        [Fact]
        public void FewExports()
        {
            TestSuccess(
@"const x1 = 1;
const x2 = 2;
const x3 = 3;
export {x1, x2, x3};");
        }

        [Fact]
        public void ManyExports()
        {
            TestSuccess(
@"const x1 = 1;
const x2 = 2;
const x3 = 3;
const x4 = 4;
const x5 = 5;
const x6 = 6;
const x7 = 7;
export {
    x1,
    x2,
    x3,
    x4,
    x5,
    x6,
    x7,
};");
        }

        [Fact]
        public void IfThenElse()
        {
            TestSuccess(
@"function f() {
    let x = 0;
    if (x === 0) {
        x = 1;
    } else {
        x = 2;
    }
}");
        }

        [Fact]
        public void TypeLiteralSmall()
        {
            TestSuccess(
                @"export const small: {a?: ""A1"" | ""A2"", b?: ""B""} = {};");
        }

        [Fact]
        public void TypeLiteralLarge()
        {
            TestSuccess(
@"export const small: {
    a?: ""A1"" | ""A2"",
    b?: ""B"",
    c?: ""C"",
    d?: ""D"",
} = {};");
        }

        [Fact]
        public void QualifierDeclarationSmall()
        {
            TestSuccess(
@"export declare const qualifier: {
    platforms: ""x64"" | ""x86"",
};");
        }

        [Fact]
        public void QualifierDeclaration()
        {
            TestSuccess(
@"export declare const qualifier: {
    platforms: ""x64"" | ""x86"",
    configurations: ""debug"" | ""release"",
    dotnetFramework: ""net461"" | ""netstandard10"",
};", preserveTrivia: true
            );
        }

        [Fact]
        public void SmallLambdaFitsOnLineLine()
        {
            TestSuccess(
@"function f() {
    return [].map(x => id(x));
}
function id(x) {
    return x;
}");
        }

        [Fact]
        public void ArraysAreSpecialInObjectLiteralsWrapAfterOneAndWhiteLineBeforeFive()
        {
            TestSuccess(
@"const x = {
    x: ['1'],
    y: [
        '2',
        '3',
    ],
    z: [
        '4',
        '5',
        '6',
        '7',
    ],
    
    zz: [
        '10',
        '11',
        '12',
        '13',
        '14',
    ],
};");
        }

        [Fact]
        public void AddIfRendersSpecial()
        {
            TestSuccess(
@"function addIf(c: boolean, ...args: string[]) : void {
}
const x = addIf(true,
    '1');
const y = addIf(false,
    '1',
    '2');",
                options: new[] { "/s+" });
        }

        [Fact]
        public void AddIfRendersNormal()
        {
            TestSuccess(
@"function addIf(c: boolean, ...args: string[]) : void {
}
const x = addIf(true, '1');
const y = addIf(false, '1', '2');",
                options: new[] { "/s-" });
        }

        [Fact]
        public void AddIfSpreadRendersSpecialInLists()
        {
            TestSuccess(
@"function addIf<T>(c: boolean, ...args: T[]) : T[] {
    return [];
}
const x = [
    1,
    2,
    3,
    
    ...addIf(true,
        10,
        11),
];",
                options: new[] { "/s+" });
        }

        [Fact]
        public void AddIfSpreadRendersNormallInLists()
        {
            TestSuccess(
@"function addIf<T>(c: boolean, ...args: T[]) : T[] {
    return [];
}
const x = [
    1,
    2,
    3,
    ...addIf(true, 10, 11),
];",
                options: new[] { "/s-" });
        }

        [Fact]
        public void Bug1021869()
        {
            TestSuccess(
@"function addIf<T>(c: boolean, f: () => T[]) : T[] {
    return f();
}
const a = {
    produces: [
        d`bb`,
        d`zz`,
        f`a/a.txt`,
        f`a/b.txt`,
        f`aa/a.txt`,
        
        ...addIf(true, () => [
            f`a.txt`,
            f`b.txt`,
            f`c.txt`,
        ]),
        
        ...addIf(true, () => [
            f`a.txt`,
        ]),
    ],
};",
                options: new[] { "/s+" });
        }
    }

    public class FakePrettyPrintAnalyzer : ScriptAnalyzer
    {
        public override AnalyzerKind Kind => AnalyzerKind.PrettyPrint;
    }
}
