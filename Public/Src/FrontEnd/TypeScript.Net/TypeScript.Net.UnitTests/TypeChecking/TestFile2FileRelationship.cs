// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using TypeScript.Net.Types;
using Xunit;
using static Test.BuildXL.TestUtilities.SimpleGraph;
using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.DScript.TypeChecking
{
    public class TestFile2FileRelationship
    {
        private readonly PathTable m_pathTable;

        private static string spec1Path = A("c", "spec1.dsc");
        private static string spec2Path = A("c", "spec2.dsc");
        private static string spec3Path = A("c", "spec3.dsc");

        public TestFile2FileRelationship()
        {
            m_pathTable = new PathTable();
        }

        [Fact]
        public void QualifierDeclarationShouldIntroduceImplicitDependency()
        {
            var spec1 = @"
export declare const qualifier: {
    configuration: 'release' | 'debug';
    platform: 'x86' | 'x64' | 'arm32' | 'arm64';
    codeAnalysisMode: 'disabled' | 'simpleAnalysis';
};";

            // Implicit dependency should be added in both cases,
            // if the file has an exmplicit namespace or not.
            var spec2 = @"
namespace X {export const x = 42;}
";

            var spec3 = @"
export const x = 42;
";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path)
                .AddSourceFileToDefaultModule(spec3, spec3Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.True(files.Spec1.HasDependency(P(spec3Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void CheckerPicksTheFirstSpecFromTheDefinitionAndShouldAddItToDependencyMap()
        {
            // Checker and Ast conversion both relying on the first declaration of the symbol.
            // Once the checker resolves identifier A.b in the third spec
            // it gets 3 definitions (for each file).
            // Currently, the checker and converter both are useing the first definition.
            string spec1 = @"export namespace A {}";
            string spec2 = @"export namespace A.C {export const b = 42;}";
            string spec3 = @"export namespace A {export const z = A.C.b;}";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path)
                .AddSourceFileToDefaultModule(spec3, spec3Path);

            var files = Analyze(checker);

            Assert.False(files.Spec1.HasDependency(P(spec3Path)));
        }

        [Fact]
        public void TemplateDeclarationShouldIntroduceImplicitDependency()
        {
            var spec1 = @"
export const template = 42;";
            var spec2 = @"
namespace X {export const x = 42;}
";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void TypeReferenceAddsImplicitDependency()
        {
            var spec1 = @"
export type Foo = number;";
            var spec2 = @"
const x: Foo = undefined;
";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void TypeAliasAddsImplicitDependency()
        {
            var spec1 = @"
export namespace X {
  export type Foo = number;
}";
            var spec2 = @"
export type Bar = X.Foo;
";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void TypeReferenceInInterfaceDeclarationAddsImplicitDependency()
        {
            var spec1 = @"
export type Foo = number;";
            var spec2 = @"
interface Bar {x: Foo};
";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void DottedIdentifierUsedFromAnotherFileAsFunctionArgumentInV2ModuleIntroducesFile2FileDependency()
        {
            var spec1 = @"
namespace X {export const x = 42;}";
            var spec2 = @"
namespace Z {export function bar(x: any) {return x;} }
export namespace Y {export const y = Z.bar({y: true, references
: [X.x]});}";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void DottedIdentifierUsedFromAnotherFileAsObjectLiteralArgumentInV2ModuleIntroducesFile2FileDependency()
        {
            var spec1 = @"
export namespace X {export const x = 42;}";
            var spec2 = @"
namespace Y {
  @@public
  export const y = {y: true, references: [X.x]};
}";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void IdentifierUsedFromAnotherFileIntroducesFile2FileDependency()
        {
            var spec1 = @"export const x = 42;";
            var spec2 = @"export const y = x;";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void IdentifierUsedInAFunctionIntroducesFile2FileDependency()
        {
            var spec1 = @"export const x = 42;";
            var spec2 = @"function foo() {return x;}";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void FunctionUsedFromAnotherFileIntroducesFile2FileDependency()
        {
            var spec1 = @"export function x() {return 42;}";
            var spec2 = @"const y = x();";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void DottedExpressionUsedFromAnotherFileIntroducesFile2FileDependency()
        {
            var spec1 = @"export namespace X {export const x = 42;}";
            var spec2 = @"export const y = X.x;";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void NameUsedFromAnotherFileInsideTheSameNamespaceIntroducesFile2FileDependency()
        {
            var spec1 = @"export namespace X {export const x = 42;}";
            var spec2 = @"export namespace X {export const y = x;}";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void IdentifierUsedFromAnotherModuleIntroducesFile2FileDependency()
        {
            var spec1 = @"@@public
export const x = 42;
";
            var spec2 = @"
import * as A from 'ModuleA';
export const y = A.x;";

            var checker = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec1, spec1Path)
                .AddSourceFile(new ModuleName("ModuleB", projectReferencesAreImplicit: true), spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void ImportStatementDoesNotIntroduceTheDependency()
        {
            var spec1 = @"@@public
export const x = 42;
";
            var spec2 = @"
import * as A from 'ModuleA';";

            var checker = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec1, spec1Path)
                .AddSourceFile(new ModuleName("ModuleB", projectReferencesAreImplicit: true), spec2, spec2Path);

            var files = Analyze(checker);

            Assert.False(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.False(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void ImportStatementIntroduceDependencyOnlyToTheFileBeingUsed()
        {
            var spec1 = @"@@public
export const x = 42;
";

            var spec2 = @"@@public
export const y = 42;
";
            var spec3 = @"
import * as A from 'ModuleA';
const r = A.x;";

            var checker = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec1, spec1Path)
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec2, spec2Path)
                .AddSourceFile(new ModuleName("ModuleB", projectReferencesAreImplicit: true), spec3, spec3Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec3Path)));

            Assert.True(files.Spec3.DependsOn(P(spec1Path)));
            Assert.False(files.Spec3.DependsOn(P(spec2Path)));
        }

        [Fact]
        public void FileWithExportShouldBeIncluded()
        {
            var spec1 = @"
  export const x = {y: 42};
";

            var spec2 = @"
export {x as y};
";
            var spec3 = @"
const r = y.y;";

            var checker = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec1, spec1Path)
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec2, spec2Path)
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec3, spec3Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec3Path)));
            Assert.True(files.Spec2.HasDependency(P(spec3Path)));
        }

        [Fact]
        public void NamespaceReferenceDoesNotIntroduceTheDependency()
        {
            var spec1 = @"export namespace X {
  export const x = 42;
}";

            var spec2 = @"export namespace X {
  export const y = 42;
}";
            var spec3 = @"
const x = X;";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path)
                .AddSourceFileToDefaultModule(spec3, spec3Path);

            var files = Analyze(checker);

            Assert.Empty(files.Spec3.UpStreamDependencies);
        }

        [Fact]
        public void NamespaceDereferenceIntroducesDependency()
        {
            var spec1 = @"export namespace X {
  export const x = 42;
}";

            var spec2 = @"export namespace X {
  export const y = 42;
}";
            var spec3 = @"
// no dependency by itsef
const x = X;
const r = x.x; // this introduces dependency
";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path)
                .AddSourceFileToDefaultModule(spec3, spec3Path);

            var files = Analyze(checker);

            Assert.True(files.Spec3.DependsOn(P(spec1Path)));
        }

        [Fact]
        public void TypeReferenceIntroduceDependenciesToAllDeclarations()
        {
            var spec1 = @"export interface X {
  x: number;
}";

            var spec2 = @"export interface X {
  y: number;
}";
            var spec3 = @"
const x: X = {x: 1, y: 2};
";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path)
                .AddSourceFileToDefaultModule(spec3, spec3Path);

            var files = Analyze(checker);

            Assert.True(files.Spec3.DependsOn(P(spec1Path)));
            Assert.True(files.Spec3.DependsOn(P(spec2Path)));
        }

        [Fact]
        public void EnumTypeReferenceIntroduceDependenciesToAllDeclarations()
        {
            var spec1 = @"export enum X {
  x = 1
}";

            var spec2 = @"export enum X {
  y = 2
}";
            var spec3 = @"
const x: X = undefined;
";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path)
                .AddSourceFileToDefaultModule(spec3, spec3Path);

            var files = Analyze(checker);

            Assert.True(files.Spec3.DependsOn(P(spec1Path)));
            Assert.True(files.Spec3.DependsOn(P(spec2Path)));
        }

        [Fact]
        public void EnumUsageIntroducesDepdenciesToAllDeclarations()
        {
            var spec1 = @"export enum X {
  x = 1
}";

            var spec2 = @"export enum X {
  y = 2
}";
            var spec3 = @"
// Enums are different from namespaces
// Any usage introduces all the declarations.
const x = X.x;
";

            var checker = CheckerWithPrelude()
                .AddSourceFileToDefaultModule(spec1, spec1Path)
                .AddSourceFileToDefaultModule(spec2, spec2Path)
                .AddSourceFileToDefaultModule(spec3, spec3Path);

            var files = Analyze(checker);

            Assert.True(files.Spec3.DependsOn(P(spec1Path)));
            Assert.True(files.Spec3.DependsOn(P(spec2Path)));
        }

        [Fact]
        public void ImportFromIntroducesDependencyOnlyToTheFileBeingUsed()
        {
            var spec1 = @"@@public
export const x = 42;
";

            var spec2 = @"@@public
export const y = 42;
";
            var spec3 = @"
const a = importFrom('ModuleA');
const r = a.x;";

            var checker = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec1, spec1Path)
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec2, spec2Path)
                .AddSourceFile(new ModuleName("ModuleB", projectReferencesAreImplicit: true), spec3, spec3Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec3Path)));

            Assert.True(files.Spec3.DependsOn(P(spec1Path)));
            Assert.False(files.Spec3.DependsOn(P(spec2Path)));
        }

        [Fact]
        public void ImportFromStatementByItselfDoesNotIntroduceADependency()
        {
            var spec1 = @"@@public
export const x = 42;
";
            var spec2 = @"
const x = importFrom('ModuleA');";

            var checker = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec1, spec1Path)
                .AddSourceFile(new ModuleName("ModuleB", projectReferencesAreImplicit: true), spec2, spec2Path);

            var files = Analyze(checker);

            Assert.False(files.Spec1.HasDependency(P(spec2Path)));
            Assert.False(files.Spec2.DependsOn(P(spec1Path)));
        }

        [Fact]
        public void IdentifierUsedFromAnotherModuleWithImportFromIntroducesFile2FileDependency()
        {
            var spec1 = @"@@public
export const x = 42;
";
            var spec2 = @"
export const y = importFrom('ModuleA').x;";

            var checker = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec1, spec1Path)
                .AddSourceFile(new ModuleName("ModuleB", projectReferencesAreImplicit: true), spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Theory]
        [InlineData("import")]
        [InlineData("export")]
        public void NamedImportOrExportIntroducesFile2FileDependency(string importOrExport)
        {
            var spec1 = @"
namespace N {
    export const x = 42;
}
";
            var spec2 = $"{importOrExport} {{N}} from 'ModuleA'";

            var checker = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("ModuleA", projectReferencesAreImplicit: true), spec1, spec1Path)
                .AddSourceFile(new ModuleName("ModuleB", projectReferencesAreImplicit: true), spec2, spec2Path);

            var files = Analyze(checker);

            Assert.True(files.Spec1.HasDependency(P(spec2Path)));
            Assert.Empty(files.Spec1.UpStreamDependencies);

            Assert.True(files.Spec2.DependsOn(P(spec1Path)));
            Assert.Empty(files.Spec2.DownStreamDependencies);
        }

        [Fact]
        public void ImportAliasesWithMultiplePackages()
        {
            var oneCoreSpec = @"
export const x = root;
";
            var oneCorePackage = @"
import * as MsWinImports from 'MsWin';
export const root = MsWinImports.root;";

            var msWinPackage = @"
@public
export const root = 42";

            string uapOneCoreSpec = A("c", "Uap", "oneCoreSpec.dsc");
            string uapPackageSpec = A("c", "Uap", "package.dsc");
            string msWinPackageSpec = A("c", "MsWin", "package.dsc");

            var checker = CheckerWithPrelude()
                .AddSourceFile(new ModuleName("Uap", projectReferencesAreImplicit: true), oneCoreSpec, uapOneCoreSpec)
                .AddSourceFile(new ModuleName("Uap", projectReferencesAreImplicit: true), oneCorePackage, uapPackageSpec)
                .AddSourceFile(new ModuleName("MsWin", projectReferencesAreImplicit: true), msWinPackage, msWinPackageSpec);

            var files = Analyze(checker);

            Assert.True(files.Spec1.DependsOn(P(uapPackageSpec)));
            Assert.True(files.Spec2.DependsOn(P(msWinPackageSpec)));
        }

        [Fact]
        public void TestBigGraphWithConcurrentTypeChecking()
        {
            var random = new Random();

            // create a random big graph representing spec dependencies
            var numNodes = 500;
            var dependencyGraph = CreateRandomBigGraph(random, numNodes);

            // construct spec files according to generated spec dependencies
            var specs = dependencyGraph
                .Nodes
                .Select(i => GenerateSpecContent(i, dependencyGraph))
                .ToList();

            var randomSpecIdx = random.Next(dependencyGraph.NodeCount);
            var expectedDownstream = dependencyGraph.OutgoingEdges(randomSpecIdx).Select(e => AbsolutePath.Create(m_pathTable, GetSpecName(e.Dest)));
            var expectedUpstream = dependencyGraph.IncomingEdges(randomSpecIdx).Select(e => AbsolutePath.Create(m_pathTable, GetSpecName(e.Src)));

            // create a checker and add generated files to it
            var checker = dependencyGraph.Nodes.Aggregate(
                CheckerWithPrelude(),
                (checkerAcc, specIdx) => checkerAcc.AddSourceFileToDefaultModule(sourceFileContent: specs[specIdx], fileName: GetSpecName(specIdx)));

            // type check
            var diagnostics = checker.RunChecker(trackFileToFileDependencies: true, degreeOfParallelism: Math.Max(Environment.ProcessorCount, 5));
            XAssert.AreEqual(0, diagnostics.Count, "Expected 0 diagnostics, got: " + string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));

            // get computed dependencies
            var sourceFile = checker.GetSourceFile(GetSpecName(randomSpecIdx));
            var asf = new AnalyzedSourceFile(checker.TypeChecker, sourceFile, checker.GetSourceFiles(), m_pathTable);
            var downstream = asf.DownStreamDependencies;
            var upstream = asf.UpStreamDependencies;

            // assert computed dependencies are correct
            var messageFormat = "Dependencies do not match: node count = {0}; chosen node = {1}; graph = {2}";
            var args = new object[] { numNodes, randomSpecIdx, dependencyGraph };
            XAssert.AreSetsEqual(expectedDownstream, downstream, true, format: messageFormat, args: args);
            XAssert.AreSetsEqual(expectedUpstream, upstream, true, format: messageFormat, args: args);
        }

        private string GetSpecName(int specIdx) => A("c", $"spec{specIdx}.dsc");

        private string GenerateSpecContent(int specIndex, SimpleGraph dependencyGraph)
        {
            var incomingEdges = dependencyGraph.IncomingEdges(specIndex).ToList();
            var sumOfImportsExpression = incomingEdges.Any()
                ? string.Join("+", incomingEdges.Select(edge => $"exportVar{edge.Src}"))
                : "0";
            var specLines = new[]
            {
                $"namespace MySpecTest {{",
                $"    export const exportVar{specIndex} = 42;",
                $"    const localVarThatDependsOnOtherSpecs{specIndex} = {sumOfImportsExpression};",
                $"}}"
            };
            return string.Join(Environment.NewLine, specLines);
        }

        private SimpleGraph CreateRandomBigGraph(Random random, int numNodes, double edgeProbability = 0.5)
        {
            var nodeRange = Enumerable.Range(0, numNodes);
            var edges = SimpleGraph
                .Product(nodeRange, nodeRange)
                .Except(IdentityRelation(numNodes))
                .Where(e => random.NextDouble() <= edgeProbability)
                .ToList();
            return new SimpleGraph(numNodes, edges);
        }

        private TestChecker CheckerWithPrelude()
        {
            return new TestChecker(defaultModuleHasImplicitSemantics: true).SetDefaultPrelude();
        }

        private TestChecker CheckerWithoutPrelude()
        {
            return new TestChecker(defaultModuleHasImplicitSemantics: false).SetDefaultPrelude();
        }

        /// <summary>
        /// Helper struct that has two analyzed files.
        /// </summary>
        private readonly struct ThreeFiles
        {
            public AnalyzedSourceFile Spec1 { get; }

            public AnalyzedSourceFile Spec2 { get; }

            public AnalyzedSourceFile Spec3 { get; }

            public ThreeFiles(PathTable pathTable, ITypeChecker checker, [NotNull]ISourceFile spec1, [NotNull]ISourceFile spec2, [CanBeNull]ISourceFile spec3, ISourceFile[] files)
                : this()
            {
                Spec1 = new AnalyzedSourceFile(checker, spec1, files, pathTable);
                Spec2 = new AnalyzedSourceFile(checker, spec2, files, pathTable);
                if (spec3 != null)
                {
                    Spec3 = new AnalyzedSourceFile(checker, spec3, files, pathTable);
                }
            }
        }

        private ThreeFiles Analyze(TestChecker checker)
        {
            var diagnostics = checker.RunChecker(trackFileToFileDependencies: true);
            if (diagnostics.Count != 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));
            }

            var files = checker.GetSourceFiles();
            var firstFile = files.Skip(1).First();
            var secondFile = files.Skip(2).First();
            var thirdFile = files.Skip(3).FirstOrDefault();

            return new ThreeFiles(m_pathTable, checker.TypeChecker, firstFile, secondFile, thirdFile, files);
        }

        private AbsolutePath P(string path)
        {
            return AbsolutePath.Create(m_pathTable, path);
        }
    }

    internal class AnalyzedSourceFile
    {
        private readonly PathTable m_pathTable;

        public HashSet<AbsolutePath> DownStreamDependencies { get; }

        public HashSet<AbsolutePath> UpStreamDependencies { get; }

        public HashSet<string> UpStreamModuleDependencies { get; }

        public AnalyzedSourceFile(ITypeChecker checker, ISourceFile sourceFile, ISourceFile[] files, PathTable pathTable)
        {
            m_pathTable = pathTable;
            DownStreamDependencies = MaterializeBitSet(checker.GetFileDependentsOf(sourceFile), files);
            UpStreamDependencies = MaterializeBitSet(checker.GetFileDependenciesOf(sourceFile), files);
            UpStreamModuleDependencies = checker.GetModuleDependenciesOf(sourceFile);
        }

        private HashSet<AbsolutePath> MaterializeBitSet(RoaringBitSet bitSet, ISourceFile[] files)
        {
            bitSet.MaterializeSetIfNeeded(string.Empty, (s, i) => files[i].GetAbsolutePath(m_pathTable));
            return bitSet.MaterializedSetOfPaths;
        }
    }

    internal static class SourceFileExtensions
    {
        public static bool DependsOn(this AnalyzedSourceFile file, AbsolutePath dependsOnCandidate)
        {
            return file.UpStreamDependencies.Contains(dependsOnCandidate);
        }

        public static bool HasDependency(this AnalyzedSourceFile file, AbsolutePath dependencyCandidate)
        {
            return file.DownStreamDependencies.Contains(dependencyCandidate);
        }
    }
}
