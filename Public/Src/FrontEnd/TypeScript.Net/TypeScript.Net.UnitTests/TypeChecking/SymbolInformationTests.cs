// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using TypeScript.Net.Binding;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.TypeChecking;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;
using Xunit.Abstractions;

namespace TypeScript.Net.UnitTests.TypeChecking
{
    public class TypeCheckerHostFake : TypeCheckerHost
    {
        private readonly ModuleName? m_scriptModuleName;
        private readonly ISourceFile[] m_sourceFiles;

        public TypeCheckerHostFake(params ISourceFile[] sourceFiles)
            : this(null, sourceFiles)
        { }

        /// <summary>
        /// If <param name="scriptModuleName"/>is passed, that module is assumed to own all <param name="sourceFiles"/>.
        /// </summary>
        public TypeCheckerHostFake(ModuleName? scriptModuleName, params ISourceFile[] sourceFiles)
        {
            m_scriptModuleName = scriptModuleName;
            m_sourceFiles = sourceFiles;
        }

        public override ICompilerOptions GetCompilerOptions()
        {
            return new CompilerOptions();
        }

        public override ISourceFile[] GetSourceFiles()
        {
            return m_sourceFiles;
        }

        public override ISourceFile GetSourceFile(string fileName)
        {
            return m_sourceFiles.FirstOrDefault(s => s.FileName == fileName);
        }

        public override bool TryGetOwningModule(string fileName, out ModuleName moduleName)
        {
            if (m_scriptModuleName.HasValue)
            {
                moduleName = m_scriptModuleName.Value;
                return true;
            }

            moduleName = ModuleName.Invalid;
            return false;
        }

        public override bool TryGetPreludeModuleName(out ModuleName preludeName)
        {
            preludeName = ModuleName.Invalid;
            return false;
        }

        public override void ReportSpecTypeCheckingCompleted(ISourceFile node, TimeSpan elapsed)
        {
        }
    }

    public class SymbolInformationTests
    {
        private const string SymbolName = "foo";
        private readonly ITestOutputHelper m_output;

        public SymbolInformationTests(ITestOutputHelper output)
        {
            m_output = output;
        }

        [Fact]
        public void Test_CreateTypeChecker()
        {
            var host = new TypeCheckerHostFake();
            var checker = Checker.CreateTypeChecker(host, true, degreeOfParallelism: 1);
            Assert.NotNull(checker);
        }

        [Fact]
        public void CheckSymbolsForObsoleteAttribute()
        {
            string code =
@"@@obsolete()
function foo() {return 42;}

// inside a function
function normal(n: any) {return foo();}

// object literal
const ol = {foo: foo()};

// normal arguments to a function
const fr = normal(foo());

// object literal as a function argument
const zz = normal({foo: foo()});

// making an alias
const alias = foo;

// alias with shorthand property assignment
const shorthand = {foo};

// function that returns an alias
function crazy() { return foo; }";

            var parsedSourceFile = ParseAndCheck(code);
            m_output.WriteLine(parsedSourceFile.ToString());
        }

        [Fact]
        public void CheckSymbolsForObsoleteAttributeOnInterfaceDeclaration()
        {
            string code =
@"
function obsolete(msg?: string) { return _ => {return msg;}; }

export interface Test {
    @@obsolete('message')
    extend: (atom: number) => number;
}";

            var diagnostics = GetSemanticDiagnostics(code);
            foreach (var d in diagnostics)
            {
                m_output.WriteLine(d.ToString());
            }
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void CheckSymbolsForObsoleteAttributeOnType()
        {
            string code =
@"@@obsolete()
interface IFoo {
    member: () => number;
}

function foo2(): IFoo {
    return {member: undefined};
}

function withArg(a: IFoo) {}

const t: IFoo = undefined;

const z = <IFoo>{member: undefined};";

            var parsedSourceFile = ParseAndCheck(code);
            Console.WriteLine(parsedSourceFile);
        }

        [Fact]
        public void TypeCheckTypePredicate()
        {
            string code =
@"interface IFoo {}
function isString(o: IFoo): o is string {
    return false;
}
";

            var diagnostics = GetSemanticDiagnostics(code);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void TypeCheckInferenceOfGenericArguments()
        {
            string code =
@"// @filename: test1.dsc
interface TestArray<T>
{
    reduce<U>(callbackfn: (previousValue: U, currentValue: T, currentIndex: number, array: TestArray<T>) => U, initialValue: U): U;
}

interface File {}

let sources = emptyArray<File>();

interface CompilationOutput {
}

function emptyCompilationOutput(): CompilationOutput {
    return undefined;
}

function emptyArray<T>(): TestArray<T> {
    return undefined;
}

namespace TestMap {
    export declare function empty<K, V>(): TestMap<K, V>;
}

interface TestMap<K,V> {
    add: (key: K, value: V) => TestMap<K, V>;
}

let outs = sources.reduce(
        (acc, src, idx) => {
            return acc.add(
                'hi',
                emptyCompilationOutput);
        },
        TestMap.empty<string, CompilationOutput>());
";

            var diagnostics = GetSemanticDiagnostics(code);
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void TypeOfLocalVariable()
        {
            string code =
@"{
    let x: number = '42';
}";

            var checkedFile = ParseAndCheck(code);
            Assert.NotNull(checkedFile);
        }

        [Fact]
        public void TypeMismatch()
        {
            string code =
@"{
    let x: number = '42';
}";

            var diagnostics = GetSemanticDiagnostics(code);
            Assert.NotEmpty(diagnostics);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetFullyQualifiedNameNamespaceExportConst(bool preserveTrivia)
        {
            string code = $"namespace A.B.C.D {{ export const {SymbolName} = 42;}}";
            CheckFullyQualifiedName(code, "A.B.C.D." + SymbolName, preserveTrivia);

            code = $"namespace A.B.C.D {{ export const {SymbolName}= 42;}}";
            CheckFullyQualifiedName(code, "A.B.C.D." + SymbolName, preserveTrivia);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetFullyQualifiedNameExportConst(bool preserveTrivia)
        {
            string code = $"export const {SymbolName} = 42;";
            CheckFullyQualifiedName(code, SymbolName, preserveTrivia);

            code = $"export const {SymbolName}= 42;";
            CheckFullyQualifiedName(code, SymbolName, preserveTrivia);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetFullyQualifiedNameNamespaceExportInterface(bool preserveTrivia)
        {
            string code = $"namespace A.B.C.D {{ export interface {SymbolName} {{}} }}";
            CheckFullyQualifiedName(code, "A.B.C.D." + SymbolName, preserveTrivia);

            code = $"namespace A.B.C.D {{ export interface {SymbolName}{{}} }}";
            CheckFullyQualifiedName(code, "A.B.C.D." + SymbolName, preserveTrivia);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetFullyQualifiedNameExportInterface(bool preserveTrivia)
        {
            string code = $"export interface {SymbolName} {{}}";
            CheckFullyQualifiedName(code, SymbolName, preserveTrivia);

            code = $"export interface {SymbolName}{{}}";
            CheckFullyQualifiedName(code, SymbolName, preserveTrivia);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetFullyQualifiedNameNamespaceExportType(bool preserveTrivia)
        {
            string code = $"namespace A.B.C.D {{ export type {SymbolName} = number; }}";
            CheckFullyQualifiedName(code, "A.B.C.D." + SymbolName, preserveTrivia);

            code = $"namespace A.B.C.D {{ export type {SymbolName}= number; }}";
            CheckFullyQualifiedName(code, "A.B.C.D." + SymbolName, preserveTrivia);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetFullyQualifiedNameExportType(bool preserveTrivia)
        {
            string code = $"export type {SymbolName} = number;";
            CheckFullyQualifiedName(code, SymbolName, preserveTrivia);

            code = $"export type {SymbolName}= number;";
            CheckFullyQualifiedName(code, SymbolName, preserveTrivia);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetFullyQualifiedNameNamespaceExportFunction(bool preserveTrivia)
        {
            string code = $"namespace A.B.C.D {{ export function {SymbolName} () {{}} }}";
            CheckFullyQualifiedName(code, "A.B.C.D." + SymbolName, preserveTrivia);

            code = $"namespace A.B.C.D {{ export function {SymbolName}() {{}} }}";
            CheckFullyQualifiedName(code, "A.B.C.D." + SymbolName, preserveTrivia);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetFullyQualifiedNameExportFunction(bool preserveTrivia)
        {
            string code = $"export function {SymbolName} () {{}}";
            CheckFullyQualifiedName(code, SymbolName, preserveTrivia);

            code = $"export function {SymbolName}() {{}}";
            CheckFullyQualifiedName(code, SymbolName, preserveTrivia);
        }

        private static void CheckFullyQualifiedName(string code, string expectedQualifiedName, bool preserveTrivia)
        {
            var sourceFile = ParseAndBind(code, preserveTrivia);
            var checker = CreateTypeChecker(sourceFile);
            checker.GetDiagnostics();

            var node = NodeWalker.TraverseBreadthFirstAndSelf(sourceFile).First(n => n is VariableDeclaration || n is FunctionDeclaration || n is InterfaceDeclaration || n is TypeAliasDeclaration) as Node;
            Assert.Equal(expectedQualifiedName, checker.GetFullyQualifiedName(node.Symbol));
        }

        private static ITypeChecker CreateTypeChecker(ISourceFile sourceFile)
        {
            var host = new TypeCheckerHostFake(sourceFile);
            return Checker.CreateTypeChecker(host, true, degreeOfParallelism: 1);
        }

        private static ISourceFile ParseAndBind(string code, bool preserveTrivia)
        {
            var sourceFile = ParsingHelper.ParseSourceFile(code, parsingOptions: new ParsingOptions(
                namespacesAreAutomaticallyExported: false,
                generateWithQualifierFunctionForEveryNamespace: false,
                preserveTrivia: preserveTrivia,
                allowBackslashesInPathInterpolation: true,
                useSpecPublicFacadeAndAstWhenAvailable: false,
                escapeIdentifiers: true));
            Binder.Bind(sourceFile, new CompilerOptions());

            return sourceFile;
        }

        private IList<Diagnostic> GetSemanticDiagnostics(string code)
        {
            var sourceFile = ParseAndBind(code, preserveTrivia: true);
            var checker = CreateTypeChecker(sourceFile);

            return checker.GetDiagnostics(sourceFile, null);
        }

        private static ISourceFile ParseAndCheck(string code)
        {
            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var checker = CreateTypeChecker(sourceFile);
            return sourceFile;
        }
    }
}
