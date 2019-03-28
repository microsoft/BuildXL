// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Incrementality;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Workspaces;
using Test.DScript.Workspaces.Utilities;
using TypeScript.Net.Types;
using Xunit;
using Test.BuildXL.FrontEnd.Core;

namespace Test.DScript.Ast.PublicSurface
{
    public class PublicSurfaceTests : WorkspaceTestBase
    {
        private readonly FrontEndStatistics m_frontEndStatistics;

        private static Regex DiscardTriviaAndAnnotations => new Regex(@"((\s)+)|(@@.+)");

        private static Regex DiscardTrivia => new Regex(@"(\s)+");

        public PublicSurfaceTests() : base(pathTable: new PathTable(), preludeName: "Sdk.Prelude", nameResolutionSemantics: NameResolutionSemantics.ImplicitProjectReferences)
        {
            m_frontEndStatistics = new FrontEndStatistics();
        }

        public static IEnumerable<object[]> VisibleTopLevelStatements()
        {
            yield return new object[] {"export const x: number = 42;", "export declare const x: number;"};
            yield return new object[]
            {
@"@@public
export const x: number = 42;",
@"@@public
export declare const x: number;"};
            yield return new object[] {"export function f() : void {}", "export declare function f() : void" };
            yield return new object[]
            {
@"export function f() : number {
    let x = 42;
    return x;
}",
"export declare function f() : number"
            };
            yield return new object[] { "export function f(a: number) : void {}", "export declare function f(a: number) : void" };
            yield return new object[]
            {
@"@@public
export function f() : void {}",
@"@@public
export declare function f() : void"};
            yield return new object[] {"export interface I {}", "export interface I {}"};
            yield return new object[] {"export type T = number;", "export type T = number;"};
            yield return new object[] {"namespace A {}", "namespace A {}"};
        }

        [Theory]
        [MemberData(nameof(VisibleTopLevelStatements))]
        [InlineData("import * as M from 'MyModule';", "import * as M from 'MyModule';")]
        public void VisibleStatementsAreKept(string spec, string expected)
        {
            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTriviaAndAnnotations(expected, result);
        }

        [Theory]
        [MemberData(nameof(VisibleTopLevelStatements))]
        public void VisibleNestedStatementsAreKept(string spec, string expected)
        {
            spec = "namespace Z {" + spec + "}";
            expected = "namespace Z {" + expected + "}";

            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTriviaAndAnnotations(expected, result);
        }

        [Theory]
        [InlineData("const x: number = 42;")]
        [InlineData("function f() : void {}")]
        public void InvisibleStatementsAreDropped(string spec)
        {
            var result = GetPublicSurface(spec);

            XAssert.AreEqual(string.Empty, result);
        }

        [InlineData("interface I {}")]
        [InlineData("type T = number;")]
        [InlineData("enum E { One }")]
        public void TypeRelatedStatementsAreKept(string spec)
        {
            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTrivia(spec, result);
        }

        [Theory]
        [InlineData("const x: number = 42; export {x};", "declare const x: number; export {x};")]
        [InlineData("interface I {} export {I};", "interface I {} export {I};")]
        [InlineData("const x: number = 42; const y: number = 32; export {x};", "declare const x: number; export {x};")]
        [InlineData("const x: number = 42; export {x as y}; export {x as z};", "declare const x: number; export {x as y}; export {x as z};")]
        [InlineData("const x: number = 42, y: boolean = true, z: string = 'hi'; export {x, y};", "declare const x: number; declare const y: boolean; export {x}; export{y};")]
        public void ImplicitlyVisibleStatementsAreKept(string spec, string expected)
        {
            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTriviaAndAnnotations(expected, result);
        }

        [Theory]
        [InlineData("export const x = 42", "export declare const x : number;")]
        [InlineData("export const x = true", "export declare const x : boolean;")]
        [InlineData("export const x = undefined", "export declare const x : any;")]
        [InlineData("export const x = (a) => a;", "export declare const x : (a: any) => any;")]
        [InlineData("export const x = (a) => 42;", "export declare const x : (a: any) => number;")]
        [InlineData("export const x = (a: number) => a;", "export declare const x : (a: number) => number;")]
        [InlineData("export const x = (a: number) => 'hi';", "export declare const x : (a: number) => string;")]
        [InlineData("function f(): number {}; export const x = f();", "export declare const x : number;")]
        [InlineData("function f() {return 42;}; export const x = f();", "export declare const x : number;")]
        [InlineData("export const x = {a: 1, b: '2'}", "export declare const x : {a: number; b: string;};")]
        [InlineData("export function f(a: string, b: boolean): void {}", "export declare function f(a: string, b: boolean): void")]
        [InlineData("export function f(a: string = 'hi', b: boolean = true): void {}", "export declare function f(a: string, b: boolean): void")]
        [InlineData("export function f(a): void {}", "export declare function f(a: any): void")]
        public void MissingTypeIsInferred(string spec, string expected)
        {
            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTriviaAndAnnotations(expected, result);
        }

        [Theory]
        [InlineData("export const x: number = undefined;")]
        [InlineData("export const x: boolean = undefined;")]
        [InlineData("export const x: string[] = undefined;")]
        [InlineData("export const x: number[][] = undefined;")]
        [InlineData("export const x: File = undefined;")]
        [InlineData("export const x: Map<string, File[]> = undefined;")]
        [InlineData("export interface A {} export const x: A = undefined;")]
        [InlineData("export interface A {} export const x: A[] = undefined;")]
        [InlineData("export interface B<T> {} export const x: B<number> = undefined;")]
        [InlineData("export interface B<T> {} export const x: B<Set<string>>[] = undefined;")]
        [InlineData("export interface A {} export interface B<T> {} export const x: B<A> = undefined;")]
        [InlineData("export const x: File | number | string = undefined;")]
        [InlineData("export interface A {} export const x: A | number[] = undefined;")]
        [InlineData("export interface A {} export interface B {} export const x: (A | B)[] = undefined;")]
        [InlineData("export const x: {f?: DerivedFile} = undefined;")]
        [InlineData("export interface A {} export const x: {a: A} = undefined;")]
        [InlineData("export const x: () => void = undefined;")]
        [InlineData("export const x: (a, b) => number = undefined;")]
        [InlineData("export const x: (a: string, b?: File) => number = undefined;")]
        public void AccessibleDeclaredTypesArePrinted(string spec)
        {
            var expected = spec.Replace("export const x", "export declare const x").Replace(" = undefined", string.Empty);
            var workspace = CreateWorkspaceFromSpecContent(spec);
            var success = TryPrintSurfaceForSpecs(
                workspace, 
                skipTypeInference: true, 
                result: out string result, 
                sourceFiles: GetSingleSpec(workspace));
            XAssert.IsTrue(success, $"Failed to get public surface of '{spec}'; expected: '{expected}'");
            AssertEqualDiscardingTriviaAndAnnotations(expected, result);
        }

        [Theory]
        [InlineData("interface A {} export const x: A = undefined;")]
        [InlineData("interface A {} export const x: A[] = undefined;")]
        [InlineData("interface A {} export const x: Set<A> = undefined;")]
        [InlineData("interface A {} export const x: Set<A[]> = undefined;")]
        [InlineData("interface B<T> {} export const x: B<number> = undefined;")]
        [InlineData("interface A {} export interface B<T> {} export const x: B<A> = undefined;")]
        [InlineData("interface A {} export interface B {} export const x: Set<(A | B)[]> = undefined;")]
        [InlineData("interface A {} export const x: () => A = undefined;")]
        [InlineData("interface A {} export const x: (a: A, b) => number = undefined;")]
        [InlineData("interface A {} export const x: (a: string, b: A | number) => void = undefined;")]
        public void InaccessibleDeclaredTypesTriggerCancellation(string spec)
        {
            var workspace = CreateWorkspaceFromSpecContent(spec);
            var success = TryPrintSurfaceForSpecs(
                workspace,
                skipTypeInference: true,
                result: out string result,
                sourceFiles: GetSingleSpec(workspace));
            XAssert.IsFalse(success, $"Expected inaccessible declared types not to be printed; instead got public surface: '{spec}'");
        }

        /// <summary>
        /// This is a case where it is important to return the right exports in a V2 module since otherwise some non-valid type accessibilty chains will
        /// be preferred over the right one. Bug #1167983.
        /// </summary>
        [Fact]
        public void MissingTypesAreCorrectlyGenerated()
        {
            const string shared = @"
namespace Shared{
    @@public
    export interface A {}
}

namespace Binary {
    @@public
    export const defaultNativeBinaryArguments: NativeBinaryArguments = undefined;

    @@public
    export interface NativeBinaryArguments {}
}";

            const string native = @"
import {Shared as Core} from 'Build.Wdg.Native.Shared';
import {Binary, Shared} from 'Build.Wdg.Native.Shared';
import * as StaticLibrary  from 'Build.Wdg.Native.Tools.StaticLibrary';

@@public
export {
    Core,
    Shared,
    StaticLibrary,
    Binary
};";
            const string staticLibrary = @"
import {Binary} from 'Build.Wdg.Native.Shared';

@@public
export const defaultArguments = Binary.defaultNativeBinaryArguments; ";

            const string test = @"
import * as NativeWdgBuild from 'Build.Wdg.Native';

@@public
export const defaultArgs = NativeWdgBuild.StaticLibrary.defaultArguments; ";

            var moduleWithContent = CreateWithPrelude(SpecEvaluationBuilder.FullPreludeContent)
                .AddContent("Build.Wdg.Native.Shared", shared)
                .AddContent("Build.Wdg.Native", native)
                .AddContent("Build.Wdg.Native.Tools.StaticLibrary", staticLibrary)
                .AddContent("Test", test);

            var workspace = GetWorkspace(moduleWithContent);
            var testSpec = workspace.GetAllSourceFiles().First(sourceFile => sourceFile.Path.AbsolutePath.Contains("Test"));
            var success = TryPrintSurfaceForSpecs(workspace, out var result, testSpec);

            XAssert.IsTrue(success);
            Assert.Contains("export declare const defaultArgs: NativeWdgBuild.Binary.NativeBinaryArguments;", result);
        }

        /// <summary>
        /// A public namespace that is part of an accessibility chain implies using the right symbol for the accessibility computation.
        /// Bug 1170277.
        /// </summary>
        [Fact]
        public void PublicNamespacesCanBePartOfAccessibilityChains()
        {
            const string exportedModule = @"
namespace N {
    @@public
    export const x : M.I = {a: 'someValue'};
}

namespace M {
    @@public
    export interface I {a: string}
}
";
            const string consumer = @"
import * as Module from 'ExportedModule';

@@public
export const t = Module.N.x;
";
            var moduleWithContent = CreateWithPrelude(SpecEvaluationBuilder.FullPreludeContent)
                .AddContent("ExportedModule", exportedModule)
                .AddContent("ConsumerModule", consumer);

            var workspace = GetWorkspace(moduleWithContent);
            var consumerSpec = workspace.GetAllSourceFiles().First(sourceFile => sourceFile.Path.AbsolutePath.Contains("ConsumerModule"));
            var success = TryPrintSurfaceForSpecs(workspace, out var result, consumerSpec);

            XAssert.IsTrue(success);
            Assert.Contains("export declare const t: Module.M.I;", result);
        }

        [Theory]
        [InlineData(@"
interface I {}
const y : I = undefined;
export const x = y;
")]
        [InlineData(@"
export const x = f();

function f() {
    interface I {};
    let t : I = {};

    return t;
}
")]
        [InlineData(@"
export function f(): A {return undefined;}

interface A {}
")]
        [InlineData(@"
export const x : A = undefined;

interface A {}
")]
        [InlineData(@"
interface I {};

export function f(a: I): void {}
")]
        public void InvalidTypeVisibilityTriggersCancellation(string spec)
        {
            var workspace = CreateWorkspaceFromSpecContent(spec);
            var sourceFile = GetSingleSpec(workspace);

            var success = TryPrintSurfaceForSpecs(workspace, out var _, sourceFile);

            XAssert.IsFalse(success);
        }

        [Fact]
        public void InvalidTypeVisibilityAcrossModulesTriggersCancellation()
        {
            var moduleWithContent = CreateWithPrelude()
                .AddContent("MySdk", @"
interface I {};

@@public
export function f() {
    let x: I = undefined;
    return x;
}")
                .AddContent("MyModule", @"
import * as Sdk from 'MySdk';
export const x = Sdk.f();
");
            var workspace = GetWorkspace(moduleWithContent);

            var success = TryPrintSurfaceForSpecs(workspace, out var _, workspace.GetAllSourceFiles());

            XAssert.IsFalse(success);
        }

        [Fact]
        public void PreludeFilesSupportPublicSurface()
        {
            var moduleWithContent = CreateWithPrelude();
            var workspace = GetWorkspace(moduleWithContent);

            var success = TryPrintSurfaceForSpecs(workspace, out var _, workspace.GetAllSourceFiles());

            XAssert.IsTrue(success);
        }

        [Fact]
        public void PreludeNameShadowingBlocksPublicSurfaceCreation()
        {
            var moduleWithContent = CreateWithPrelude()
                .AddContent("PreludeFake", @"
namespace SdkPrelude {
    @@public 
    export type TestInterface = TestPrelude.TestInterface;
}
").AddContent("Test", @"
import {SdkPrelude} from 'PreludeFake';

export namespace TestPrelude
{
    @@public 
    export const env : SdkPrelude.TestInterface = undefined;
    
    // This one should block public surface to be created
    // 'test.value' is of type TestPrelude.TestType, but
    // 'TestPrelude' is shadowed by this namespace
    @@public
    export const test = env.value; 
}
");
            var workspace = GetWorkspace(moduleWithContent);

            var success = TryPrintSurfaceForSpecs(workspace, out var _, workspace.GetAllSourceFiles());

            XAssert.IsFalse(success);
        }

        [Fact]
        public void ExportStarDeclarationWithPublicModifierShouldStayPublic()
        {
            var moduleWithContent = CreateWithPrelude()
                .AddContent("MySdk", @"
@@public
export interface I {};

@@public
export function f() {
    let x: I = undefined;
    return x;
}")
                .AddContent("MyModule", @"
@@public
export * from 'MySdk';
");
            var workspace = GetWorkspace(moduleWithContent);

            var success = TryPrintSurfaceForSpecs(workspace, out var _, workspace.GetAllSourceFiles());

            XAssert.IsTrue(success);
        }

        [Fact]
        public void ExportDeclarationWithPublicModifierShouldStayPublic()
        {
            string spec = @"
interface I {};
@@public
export {I};
";
            var result = GetPublicSurface(spec);

            string expected = @"
@@2interface I {}
@@37
@@public
export {I};";
            AssertEqualDiscardingTrivia(expected, result);
        }

        [Fact]
        public void AliasedExportDeclarationWithPublicModifierShouldStayPublic()
        {
            string spec = @"
interface I {};
@@public
export {I as X};
";
            var result = GetPublicSurface(spec);

            string expected = @"
@@2interface I {}
@@37
@@public
export {I as X};";
            AssertEqualDiscardingTrivia(expected, result);
        }


        [Fact]
        public void ShadowingNamespaceLocallyShouldBlockTypeVisibility()
        {
            var moduleWithContent = CreateWithPrelude()
                .AddContent(
                    "MyApp", @"
function createAssembly(): Core.Assembly {
    return undefined;
}
    
namespace System.Core {
    @@public
    export const dll = createAssembly();
}


namespace Core {
    @@public
    export interface Assembly {
        _branding: string;
    }
}");
                
            var workspace = GetWorkspace(moduleWithContent);
            var success = TryPrintSurfaceForSpecs(workspace, out var _, workspace.GetAllSourceFiles());

            XAssert.IsFalse(success);
        }

        [Fact]
        public void ShadowingNamespaceAcrossSpecsShouldBlockTypeVisibility()
        {
            var moduleWithContent = CreateWithPrelude()
                .AddContent(
                    "MyApp", @"
function createAssembly(): Core.Assembly {
    return undefined;
}
    
namespace System.Core {
    @@public
    export const dll = createAssembly();
}", 

@"
namespace Core {
    @@public
    export interface Assembly {
        _branding: string;
    }
}");

            var workspace = GetWorkspace(moduleWithContent);
            var success = TryPrintSurfaceForSpecs(workspace, out var _, workspace.GetAllSourceFiles());

            XAssert.IsFalse(success);
        }

        [Theory]
        [InlineData("export const enum A {}", @"
@@0
export const enum A {}")]
        [InlineData("export const enum A { One, Two}", @"
@@0
export const enum A {
    @@21
    One,

    @@26
    Two,
}")]
        [InlineData("export function f() : void {}", @"
@@0
export declare function f() : void")]
        [InlineData("export interface I {}", @"
@@0
export interface I {}")]
        [InlineData("namespace A {}", @"
@@0
namespace A {}")]
        [InlineData("export type T = number;", @"
@@0
export type T = number;")]
        public void PositionsAreAnnotatedForDeclarationStatements(string spec, string expected)
        {
            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTrivia(expected, result);
        }

        [Theory]
        [InlineData("export const x: number = 42;", @"
@@12
export declare const x : number;")]
        [InlineData("export const x: number = 42, y: boolean = true;", @"
@@12
export declare const x : number;
@@28
export declare const y : boolean;")]
        public void PositionsAreAnnotatedForVariableStaments(string spec, string expected)
        {
            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTrivia(expected, result);
        }

        [Theory]
        [InlineData(
@"const x: number = 42;
export{x};", 
@"@@5
declare const x : number;
@@30
export{x};")]
        [InlineData(
@"const x: number = 42;
export{x, x as y};",
@"@@5
declare const x : number;
@@30
export{x};
@@32
export{x as y};")]
        public void PositionsAreAnnotatedForExportStatements(string spec, string expected)
        {
            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTrivia(expected, result);
        }

        [Theory]
        [InlineData("const x: number = 42; export {x};", @"
@@5
declare const x : number;
@@30
export {x};")]
        [InlineData("const x: number = 42, y: boolean = true; export {y}", @"
@@21
declare const y : boolean;
@@49
export {y};")]
        [InlineData("interface I {} export {I as J};", @"
@@0
interface I {}
@@23
export {I as J};")]
        public void PositionsAreAnnotatedForImplicitlyExportedDeclarations(string spec, string expected)
        {
            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTrivia(expected, result);
        }

        [Fact]
        public void PositionsAreAnnotatedFirstOnTopOfExistingDecorators()
        {
            var spec = @"
@@public
export const x = 53;";

            var expected = @"
@@24
@@public
export declare const x: number;";
            
            var result = GetPublicSurface(spec);

            AssertEqualDiscardingTrivia(expected, result);
        }

        [Theory]
        [InlineData(@"
export const x = 42;
export const result = x;
")]
        #region helpers

        protected string GetPublicSurface(string specContent)
        {
            var workspace = CreateWorkspaceFromSpecContent(specContent);

            return PrintSurfaceForSingleSpec(workspace);
        }

        private Workspace CreateWorkspaceFromSpecContent(string specContent)
        {
            var moduleWithContent = CreateWithPrelude(SpecEvaluationBuilder.FullPreludeContent).AddContent("MyModule", specContent);
            var workspace = GetWorkspace(moduleWithContent);
            return workspace;
        }

        private Workspace GetWorkspace(ModuleRepository moduleRepository)
        {
            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleRepository);
            var workspace = workspaceProvider.CreateWorkspaceFromAllKnownModulesAsync().GetAwaiter().GetResult();

            var semanticWorkspaceProvider = new SemanticWorkspaceProvider(m_frontEndStatistics, workspace.WorkspaceConfiguration);
            var semanticWorkspace = semanticWorkspaceProvider.ComputeSemanticWorkspaceAsync(PathTable, workspace).GetAwaiter().GetResult();

            var semanticModel = semanticWorkspace.GetSemanticModel();

            if (semanticModel.GetAllSemanticDiagnostics().Any())
            {
                var errors = string.Join("\r\n", semanticModel.GetAllSemanticDiagnostics());
                XAssert.Fail("Semantic model is expected to be error free.  Errors found " + errors);
            }

            return semanticWorkspace;
        }

        private static string PrintSurfaceForSingleSpec(Workspace workspace)
        {
            var sourceFile = GetSingleSpec(workspace);

            var success = TryPrintSurfaceForSpecs(workspace, out string result, sourceFile);
            
            XAssert.IsTrue(success);

            return result;
        }

        private static ISourceFile GetSingleSpec(Workspace workspace)
        {
            var modules = workspace.SpecModules.ToList();
            XAssert.AreEqual(1, modules.Count, "Expected to find a single module (+ the prelude module)");

            var singleModule = modules.First();
            XAssert.AreEqual(1, singleModule.Specs.Count, "Expect a single spec in the module");

            var sourceFile = singleModule.Specs.Values.First().GetSourceFile();
            return sourceFile;
        }

        private static bool TryPrintSurfaceForSpecs(Workspace workspace, out string result, params ISourceFile[] sourceFiles)
        {
            return TryPrintSurfaceForSpecs(workspace, false, out result, sourceFiles);
        }

        private static bool TryPrintSurfaceForSpecs(Workspace workspace, bool skipTypeInference, out string result, params ISourceFile[] sourceFiles)
        {
            using (var writer = new ScriptWriter())
            {
                var printer = new PublicSurfacePrinter(writer, workspace.GetSemanticModel(), skipTypeInferenceForTesting: skipTypeInference);

                foreach (var sourceFile in sourceFiles)
                {
                    ((IVisitableNode)sourceFile).Accept(printer);
                }

                result = writer.ToString();

                // If the CancellationRequest is true, then the error happen during public facade computation
                return !printer.CancellationRequested;
            }
        }

        protected static void AssertEqualDiscardingTriviaAndAnnotations(string expected, string result)
        {
            AssertEqualModuleRegex(expected, result, DiscardTriviaAndAnnotations);
        }

        protected static void AssertEqualDiscardingTrivia(string expected, string result)
        {
            AssertEqualModuleRegex(expected, result, DiscardTrivia);
        }

        private static void AssertEqualModuleRegex(string expected, string result, Regex regex)
        {
            XAssert.AreEqual(regex.Replace(expected, string.Empty), regex.Replace(result, string.Empty));
        }

        #endregion
    }
}
