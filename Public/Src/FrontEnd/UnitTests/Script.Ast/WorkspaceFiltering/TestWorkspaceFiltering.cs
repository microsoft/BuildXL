// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Core.Incrementality;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips.Filter;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.Core;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;
using Diagnostic = TypeScript.Net.Diagnostics.Diagnostic;
using ISymbol = TypeScript.Net.Types.ISymbol;
using SymbolTable = BuildXL.Utilities.Core.SymbolTable;

namespace Test.DScript.Ast.WorkspaceFiltering
{
    public class TestWorkspaceFiltering
    {
        private StringTable StringTable => m_pathTable.StringTable;
        private readonly PathTable m_pathTable = new PathTable();
        private readonly SymbolTable m_symbolTable;

        private readonly string specFile = A("d", "spec1.dsc");
        private readonly string specFile2 = A("d", "spec2.dsc");
        private readonly string specFile3 = A("d", "spec3.dsc");
        private readonly string baseSpec = A("d", "baseSpec.dsc");
        private readonly string baseSpec2 = A("d", "baseSpec2.dsc");
        private readonly string myModule = A("d", "myModule.dsc");
        private readonly string myModule2 = A("d", "myModule2.dsc");
        private readonly string myDerivedModule = A("d", "myDerivedModule.dsc");
        private readonly string baseScrubDir = A("d", "base");
        private readonly string myModuleScrubDir = A("d", "mymodule");
        private readonly string myModule2ScrubDir = A("d", "mymodule2");

        /// <inheritdoc />
        public TestWorkspaceFiltering()
        {
            m_symbolTable = new SymbolTable(m_pathTable.StringTable);
        }

        [Fact]
        public void ScrubDirectoriesIncludeJustTargetWorkspaceTheirDependencies()
        {
            /*
             * MyModule and MyModule2 both depend on BaseModule
             * Each of these 3 modules declares a different scrub directory.
             * When filtering to MyModule, we expect the scrub dir for MyModule and BaseModule to be included in the graph, but not the scrub directory for MyModule2
             */
            // Base module. Root spec
            var baseModule = ModuleDescriptor.CreateForTesting("baseModuleDescriptor");
            var baseModuleSourceFile = SourceFile(baseSpec);
            var baseScrubDir = AbsolutePath.Create(m_pathTable, this.baseScrubDir);

            // MyModule: depends on a spec from the base module
            var moduleDescriptor = ModuleDescriptor.CreateForTesting("MyModule");
            var mySpecPath = myModule;
            var moduleSourceFile = SourceFile(mySpecPath);
            var myModuleScrubDir = AbsolutePath.Create(m_pathTable, this.myModuleScrubDir);

            // MyModule2: also depends on a spec from the base module
            var moduleDescriptor2 = ModuleDescriptor.CreateForTesting("MyModule2");
            var mySpecPath2 = myModule2;
            var moduleSourceFile2 = SourceFile(mySpecPath2);
            var myModule2ScrubDir = AbsolutePath.Create(m_pathTable, this.myModule2ScrubDir);

            var workspace = CreateWorkspace(
                CreateEmptyParsedModuleWithScrubDirectories(moduleDescriptor, [moduleSourceFile], myModuleScrubDir),
                CreateEmptyParsedModuleWithScrubDirectories(baseModule, [baseModuleSourceFile], baseScrubDir),
                CreateEmptyParsedModuleWithScrubDirectories(moduleDescriptor2, [moduleSourceFile2], myModule2ScrubDir));

            // Can add dependencies only when the workspace is constructed
            AddUpStreamDependency(workspace, moduleSourceFile, baseSpec);
            AddUpStreamModuleDependency(moduleSourceFile, "baseModuleDescriptor");

            AddUpStreamDependency(workspace, moduleSourceFile2, baseSpec);
            AddUpStreamModuleDependency(moduleSourceFile2, "baseModuleDescriptor");

            // Filter takes only myModule.dsc
            var filter = ModuleFilterByModuleName("MyModule");

            // Act
            FilterWorkspace(workspace, filter);

            var moduleFromFilteredWorksapce = workspace.Modules;
            Assert.NotNull(moduleFromFilteredWorksapce);

            Assert.Equal(workspace.ModuleCount, 2);
            
            var scrubDirectories = workspace.Modules.SelectMany(module => module.Definition.ScrubDirectories).ToList();
            Assert.Equal(scrubDirectories.Count, 2);

            Assert.True(scrubDirectories.Contains(baseScrubDir));
            Assert.True(scrubDirectories.Contains(myModuleScrubDir));
        }

        [Fact]
        public void TargetWorkspaceHasOneFileFromTheCurrentModuleAndTheirDependencies()
        {
            // Arrange
            // Base module. Root spec
            var baseModule = ModuleDescriptor.CreateForTesting("MyBaseModule");
            var baseModuleSourceFile = SourceFile(baseSpec);
            var baseModuleSourceFile2 = SourceFile(baseSpec2);

            // MyModule: depends on a spec from the base module
            var moduleDescriptor = ModuleDescriptor.CreateForTesting("MyModule");
            var mySpecPath = myModule;
            var moduleSourceFile = SourceFile(mySpecPath);

            var moduleSourceFile2 = SourceFile(myModule2);

            // MyDerivedModule: depends on MyModule spec
            var derivedModule = ModuleDescriptor.CreateForTesting("MyDerivedModule");
            var myDerivedSourceFile = SourceFile(myDerivedModule);

            var workspace = CreateWorkspace(
                CreateEmptyParsedModule(moduleDescriptor, moduleSourceFile, moduleSourceFile2),
                CreateEmptyParsedModule(baseModule, baseModuleSourceFile, baseModuleSourceFile2),
                CreateEmptyParsedModule(derivedModule, myDerivedSourceFile));

            // Can add dependencies only when the workspace is constructed
            AddUpStreamDependency(workspace, moduleSourceFile, baseSpec);
            AddUpStreamModuleDependency(moduleSourceFile, "MyBaseModule");

            AddUpStreamDependency(workspace, myDerivedSourceFile, mySpecPath);
            AddUpStreamModuleDependency(myDerivedSourceFile, "MyModule");

            // Filter takes only myModule.dsc
            var filter = ModuleFilterBySpecFullPath(mySpecPath);

            // Act
            FilterWorkspace(workspace, filter);

            // Assert
            var moduleFromFilteredWorksapce = workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyModule");
            Assert.NotNull(moduleFromFilteredWorksapce);

            // MyModule in filtered workspace has just one spec
            Assert.Equal(moduleFromFilteredWorksapce.Specs.Count, 1);
            Assert.Equal(moduleFromFilteredWorksapce.Specs.First().Value, moduleSourceFile);

            // Filtered workspace has the base module as well, because there is a dependency between MyModule and BaseModule
            var baseModuleFromFilteredWorkspace = workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyBaseModule");
            Assert.NotNull(baseModuleFromFilteredWorkspace);

            // MyBaseModule in filtered workspace has just one spec
            Assert.Equal(baseModuleFromFilteredWorkspace.Specs.Count, 1);
            Assert.Equal(baseModuleFromFilteredWorkspace.Specs.First().Value, baseModuleSourceFile);
        }

        [Fact]
        public void TargetWorkspaceHasAllTransitiveDependencies()
        {
            // Arrange
            // One module:
            // Spec3 -> Spec2 -> Spec1
            var moduleDescriptor = ModuleDescriptor.CreateForTesting("MyModule");
            var spec1 = SourceFile(specFile);

            var spec2 = SourceFile(specFile2);

            var spec3 = SourceFile(specFile3);

            var workspace = CreateWorkspace(
                CreateEmptyParsedModule(moduleDescriptor, spec1, spec2, spec3));

            AddUpStreamDependency(workspace, spec2, specFile);
            AddUpStreamDependency(workspace, spec3, specFile2);

            // Filter takes only myModule.dsc
            var filter = ModuleFilterBySpecFullPath(specFile3);

            // Act
            FilterWorkspace(workspace, filter);

            // Assert
            var moduleFromFilteredWorksapce = workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyModule");
            Assert.NotNull(moduleFromFilteredWorksapce);

            Assert.Equal(moduleFromFilteredWorksapce.Specs.Count, 3);
        }

        [Fact]
        public void TargetWorkspaceHasFullModulesAndPartialModulesBasedOnAFilter()
        {
            // Arrange
            // Base module. Root spec
            var baseModule = ModuleDescriptor.CreateForTesting("MyBaseModule");
            var baseModuleSourceFile = SourceFile(baseSpec);
            var baseModuleSourceFile2 = SourceFile(baseSpec2);

            // MyModule: depends on a spec from the base module
            var moduleDescriptor = ModuleDescriptor.CreateForTesting("MyModule");
            var mySpecPath = myModule;
            var moduleSourceFile = SourceFile(mySpecPath);

            var moduleSourceFile2 = SourceFile(myModule2);

            // MyDerivedModule: depends on MyModule spec
            var derivedDescriptor = ModuleDescriptor.CreateForTesting("MyDerivedModule");
            var myDerivedSourceFile = SourceFile(myDerivedModule);

            var workspace = CreateWorkspace(
                CreateEmptyParsedModule(moduleDescriptor, moduleSourceFile, moduleSourceFile2),
                CreateEmptyParsedModule(baseModule, baseModuleSourceFile, baseModuleSourceFile2),
                CreateEmptyParsedModule(derivedDescriptor, myDerivedSourceFile));

            AddUpStreamDependency(workspace, moduleSourceFile, baseSpec);
            AddUpStreamDependency(workspace, myDerivedSourceFile, mySpecPath);

            // Filter takes base module and one spec from the derived one
            var filter = new EvaluationFilter(
                m_symbolTable,
                m_pathTable,
                valueNamesToResolve: CollectionUtilities.EmptyArray<FullSymbol>(),
                valueDefinitionRootsToResolve: new List<AbsolutePath>() {AbsolutePath.Create(m_pathTable, mySpecPath)},
                modulesToResolver: new List<StringId>() {StringId.Create(StringTable, "MyBaseModule")});

            // Act
            FilterWorkspace(workspace, filter);

            // Assert
            var moduleFromFilteredWorksapce = workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyModule");
            Assert.NotNull(moduleFromFilteredWorksapce);

            // MyModule in filtered workspace has just one spec
            Assert.Equal(moduleFromFilteredWorksapce.Specs.Count, 1);
            Assert.Equal(moduleFromFilteredWorksapce.Specs.First().Value, moduleSourceFile);

            // Filtered workspace has the base module as well, because there is a dependency between MyModule and BaseModule
            var baseModuleFromFilteredWorkspace = workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyBaseModule");
            Assert.NotNull(baseModuleFromFilteredWorkspace);

            // Both specs from the base module shoudl be presented.
            Assert.Equal(baseModuleFromFilteredWorkspace.Specs.Count, 2);
        }

        [Fact]
        public void TargetWorkspaceHasNonFilteredModuleAndItsUpstreamDependency()
        {
            // Arrange

            // Base module. The root module with no dependencies.
            var baseModule = ModuleDescriptor.CreateForTesting("MyBaseModule");

            // MyModule depends on BaseModule
            var moduleDescriptor = ModuleDescriptor.CreateForTesting("MyModule");
            var moduleSourceFile = SourceFile(myModule);
            moduleSourceFile.AddModuleDependency("MyBaseModule");

            // MyDerived module depends on MyModule
            var derivedDescriptor = ModuleDescriptor.CreateForTesting("MyDerivedModule");
            var myDerivedSourceFile = SourceFile(myDerivedModule);
            myDerivedSourceFile.AddModuleDependency("MyModule");

            var workspace = CreateWorkspace(
                CreateEmptyParsedModule(moduleDescriptor, moduleSourceFile),
                CreateEmptyParsedModule(baseModule),
                CreateEmptyParsedModule(derivedDescriptor, myDerivedSourceFile));

            var filter = ModuleFilterByModuleName("MyModule");

            // Act
            FilterWorkspace(workspace, filter);

            // Assert
            Assert.NotNull(workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyModule"));
            Assert.NotNull(workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyBaseModule"));
            Assert.Null(workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyDerivedModule"));
        }

        [Fact]
        public void TargetWorkspaceHasAllModulesFromFilter()
        {
            // Arrange
            var myOtherModule = ModuleDescriptor.CreateForTesting("MyOtherModule");
            var myModule = ModuleDescriptor.CreateForTesting("MyModule");
            var workspace = CreateWorkspace(
                CreateEmptyParsedModule(myModule),
                CreateEmptyParsedModule(myOtherModule));

            var filter = ModuleFilterByModuleName("MyModule");

            // Act
            FilterWorkspace(workspace, filter);

            // Assert
            Assert.NotNull(workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyModule"));

            // MyModule and MyOtherModule are not related, so the "other" module should be filtered out.
            Assert.Null(workspace.SpecModules.FirstOrDefault(m => m.Descriptor.Name == "MyOtherModule"));
        }

        private EvaluationFilter ModuleFilterByModuleName(string moduleName)
        {
            return new EvaluationFilter(
                m_symbolTable,
                m_pathTable,
                CollectionUtilities.EmptyArray<FullSymbol>(),
                CollectionUtilities.EmptyArray<AbsolutePath>(),
                new List<StringId>() {StringId.Create(StringTable, moduleName)});
        }

        private EvaluationFilter ModuleFilterBySpecFullPath(string filePath)
        {
            return new EvaluationFilter(
                m_symbolTable,
                m_pathTable,
                CollectionUtilities.EmptyArray<FullSymbol>(),
                new List<AbsolutePath>() {AbsolutePath.Create(m_pathTable, filePath)},
                CollectionUtilities.EmptyArray<StringId>());
        }

        private Workspace CreateWorkspace(params ParsedModule[] parsedModules)
        {
            var workspace = new Workspace(null, EmptyConfiguration, parsedModules, Enumerable.Empty<Failure>(), Prelude(), ConfigurationModule());
            return new WorkspaceWithFakeSemanticModel(workspace);
        }

        private void FilterWorkspace(Workspace workspace, EvaluationFilter filter)
        {
            var workspaceFilter = new WorkspaceFilter(m_pathTable);
            workspace.FilterWorkspace(workspaceFilter.FilterForConversion(workspace, filter));
        }

        private ParsedModule Prelude()
        {
            return CreateEmptyParsedModule(ModuleDescriptor.CreateForTesting("Sdk.Prelude"));
        }

        private ParsedModule ConfigurationModule()
        {
            return CreateEmptyParsedModule(ModuleDescriptor.CreateForTesting(Names.ConfigModuleName));
        }

        private ParsedModule CreateEmptyParsedModule(ModuleDescriptor moduleDescriptor)
        {
            var moduleDefinition = CreateModuleDefinition(moduleDescriptor, Enumerable.Empty<AbsolutePath>(), Enumerable.Empty<AbsolutePath>());

            return new ParsedModule(moduleDefinition, new Dictionary<AbsolutePath, ISourceFile>(), new ReadOnlyHashSet<(ModuleDescriptor, Location)>());
        }

        private SourceFile SourceFile(string path)
        {
            var source = new SourceFile()
                   {
                       Path = Path.Absolute(path),
                   };

            source.InitDependencyMaps(0, 1);

            return source;
        }

        private ParsedModule CreateEmptyParsedModule(ModuleDescriptor moduleDescriptor, params ISourceFile[] specs)
        {
            var moduleDefinition = CreateModuleDefinition(moduleDescriptor, specs.Select(s => s.GetAbsolutePath(m_pathTable)), Enumerable.Empty<AbsolutePath>());

            return new ParsedModule(
                moduleDefinition,
                specs.ToDictionary(s => AbsolutePath.Create(m_pathTable, s.Path.AbsolutePath), s => s),
                new ReadOnlyHashSet<(ModuleDescriptor, Location)>());
        }

        private ParsedModule CreateEmptyParsedModuleWithScrubDirectories(ModuleDescriptor moduleDescriptor, ISourceFile[] specs, params AbsolutePath[] scrubDirectories)
        {
            var moduleDefinition = CreateModuleDefinition(moduleDescriptor, specs.Select(s => s.GetAbsolutePath(m_pathTable)), scrubDirectories);

            return new ParsedModule(
                moduleDefinition,
                specs.ToDictionary(s => AbsolutePath.Create(m_pathTable, s.Path.AbsolutePath), s => s),
                new ReadOnlyHashSet<(ModuleDescriptor, Location)>());
        }

        private ModuleDefinition CreateModuleDefinition(ModuleDescriptor moduleDescriptor, IEnumerable<AbsolutePath> specs, IEnumerable<AbsolutePath> scrubDirectories)
        {
            var moduleDefinition = new ModuleDefinition(
                moduleDescriptor,
                AbsolutePath.Create(m_pathTable, A("d", "temp")),
                AbsolutePath.Invalid,
                AbsolutePath.Create(m_pathTable, A("d", "module.config.dsc")),
                specs,
                NameResolutionSemantics.ImplicitProjectReferences,
                v1QualifierSpaceId: 0,
                allowedModuleDependencies: null,
                cyclicalFriendModules: null,
                mounts: null,
                scrubDirectories: scrubDirectories);
            return moduleDefinition;
        }

        private static WorkspaceConfiguration EmptyConfiguration { get; } = WorkspaceConfiguration.CreateForTesting();

        private void AddUpStreamDependency(Workspace workspace, SourceFile sourceFile, string dependency)
        {
            var currentIndex = GetIndexFor(workspace, sourceFile);
            sourceFile.InitDependencyMaps(currentIndex, workspace.SpecCount);

            var dependencyIndex = GetIndexFor(workspace, dependency);
            sourceFile.AddFileDependency(dependencyIndex);
        }

        private void AddUpStreamModuleDependency(SourceFile sourceFile, string dependency)
        {
            sourceFile.ModuleDependencies.Add(dependency);
        }

        private int GetIndexFor(Workspace workspace, SourceFile sourceFile)
        {
            return workspace.GetAllSourceFiles().IndexOf(sourceFile);
        }

        private int GetIndexFor(Workspace workspace, string path)
        {
            var sources = workspace.GetAllSourceFiles();
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i].Path.AbsolutePath == path)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    internal class WorkspaceWithFakeSemanticModel : Workspace
    {
        private class FakeSemanticModel : ISemanticModel
        {
            public ITypeChecker TypeChecker
            {
                get
                {
                    throw new NotImplementedException();
                }
            }

            public IEnumerable<Diagnostic> GetTypeCheckingDiagnosticsForFile(ISourceFile file)
            {
                Contract.Requires(file != null);
                throw new NotImplementedException();
            }

            public IEnumerable<Diagnostic> GetAllSemanticDiagnostics()
            {
                throw new NotImplementedException();
            }

            public IType GetCurrentQualifierType(INode currentNode)
            {
                Contract.Requires(currentNode != null, "currentNode != null");
                throw new NotImplementedException();
            }

            public INode GetCurrentQualifierDeclaration(INode currentNode)
            {
                Contract.Requires(currentNode != null, "currentNode != null");
                throw new NotImplementedException();
            }

            RoaringBitSet ISemanticModel.GetFileDependentFilesOf(ISourceFile sourceFile)
            {
                Contract.Requires(sourceFile != null);
                return sourceFile.FileDependents;
            }

            RoaringBitSet ISemanticModel.GetFileDependenciesOf(ISourceFile sourceFile)
            {
                Contract.Requires(sourceFile != null);
                return sourceFile.FileDependencies;
            }

            public string GetFullyQualifiedName(ISymbol symbol)
            {
                Contract.Requires(symbol != null, "symbol != null");
                throw new NotImplementedException();
            }

            public ISymbol GetAliasedSymbol(ISymbol symbol, bool resolveAliasRecursively = true)
            {
                Contract.Requires(symbol != null, "symbol != null");
                throw new NotImplementedException();
            }

            public ISymbol GetShorthandAssignmentValueSymbol(INode location)
            {
                Contract.Requires(location != null, "location != null");
                throw new NotImplementedException();
            }

            public ISymbol GetSymbolAtLocation(INode node)
            {
                Contract.Requires(node != null, "node != null");
                throw new NotImplementedException();
            }

            public IType GetTypeAtLocation(INode node)
            {
                Contract.Requires(node != null, "node != null");
                throw new NotImplementedException();
            }

            public ISymbol GetTemplateAtLocation(INode node)
            {
                Contract.Requires(node != null, "node != null");
                throw new NotImplementedException();
            }

            public HashSet<string> GetModuleDependentsOf(ISourceFile sourceFile)
            {
                Contract.Requires(sourceFile != null);
                return ((SourceFile)sourceFile).ModuleDependencies;
            }

            public bool IsNamespaceType(INode currentNode)
            {
                Contract.Requires(currentNode != null, "currentNode != null");
                throw new NotImplementedException();
            }

            public bool IsNamespaceType(ISymbol symbol)
            {
                Contract.Requires(symbol != null, "symbol != null");
                throw new NotImplementedException();
            }

            public string TryGetResolvedModulePath(ISourceFile sourceFile, string referencedModuleName)
            {
                Contract.Requires(sourceFile != null);
                Contract.Requires(referencedModuleName != null);
                throw new NotImplementedException();
            }

            public bool TryPrintType(IType type, out string result, INode enclosingDeclaration = null, TypeFormatFlags flags = TypeFormatFlags.None)
            {
                Contract.Requires(type != null, "type != null");
                throw new NotImplementedException();
            }

            public bool TryPrintReturnTypeOfSignature(
                ISignatureDeclaration signatureDeclaration,
                out string result,
                INode enclosingDeclaration = null,
                TypeFormatFlags flags = TypeFormatFlags.None)
            {
                Contract.Requires(signatureDeclaration != null, "signatureDeclaration != null");
                throw new NotImplementedException();
            }

            public void FilterWasApplied(HashSet<ISourceFile> filteredOutSpecs)
            {
                Contract.Requires(filteredOutSpecs != null, "filteredOutSpecs != null");
            }

            public IDeclaration GetFirstNotFilteredDeclarationOrDefault(ISymbol resolvedSymbol)
            {
                return null;
            }
        }

        public WorkspaceWithFakeSemanticModel(Workspace workspace)
            : base(null, workspace.WorkspaceConfiguration, workspace.SpecModules, Enumerable.Empty<Failure>(), workspace.PreludeModule, workspace.ConfigurationModule)
        {
        }

        public override ISemanticModel GetSemanticModel()
        {
            return new FakeSemanticModel();
        }
    }

    internal static class SourceFileExtensions
    {
    }
}
