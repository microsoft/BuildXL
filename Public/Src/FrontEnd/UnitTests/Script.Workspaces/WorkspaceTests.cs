// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Workspaces.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Workspaces
{
    public class WorkspaceTests : WorkspaceTestBase
    {
        private readonly ITestOutputHelper m_output;

        public WorkspaceTests(ITestOutputHelper output)
        {
            m_output = output;
        }

        [Fact]
        public async Task CreateWorkspaceForAModule()
        {
            var moduleReference = ModuleDescriptor.CreateForTesting("MyModule");
            var moduleWithContent = CreateEmptyContent().AddContent(moduleReference, "return 1;");

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent);
            var workspace = await workspaceProvider.CreateWorkspaceFromModuleAsync(moduleReference);

            // The module should have the right name and the expected parsed spec
            var module = AssertSuccessAndGetFirstModule(workspace);
            XAssert.AreEqual(moduleReference, module.Descriptor);

            // There should be one spec
            XAssert.AreEqual(1, module.Specs.Count);
        }

        [Fact]
        public async Task CreateWorkspaceForAllModules()
        {
            var moduleWithContent = CreateEmptyContent()
                .AddContent("Module1", "return 1;", "return 2;")
                .AddContent("Module2", "return 3;", "return 4;");

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent);
            var workspace = await workspaceProvider.CreateWorkspaceFromAllKnownModulesAsync();

            var modules = AssertSuccessAndGetAllModules(workspace);

            // There should be two modules
            XAssert.AreEqual(2, modules.Count);

            // Each module should have two parsed specs
            foreach (var module in modules)
            {
                XAssert.AreEqual(2, module.Specs.Count);
            }
        }

        [Fact]
        public async Task CreateWorkspaceForASpec()
        {
            var moduleReference = ModuleDescriptor.CreateForTesting("MyModule");
            var moduleWithContent = CreateEmptyContent().AddContent(moduleReference, "return 1;");
            var pathToFirstSpec = moduleWithContent.GetPathToModuleAndSpec(moduleReference, 0);

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent);

            var workspace = await workspaceProvider.CreateWorkspaceFromSpecAsync(pathToFirstSpec);

            var module = AssertSuccessAndGetFirstModule(workspace);
            XAssert.AreEqual(moduleReference, module.Descriptor);
        }

        [Fact]
        public async Task ResolversAreTraversedInOrder()
        {
            var moduleWithContent = CreateEmptyContent()
                .AddContent("MyModule", "return 1;", "return 2;");

            var sameModuleWithContent = CreateEmptyContent()
                .AddContent("MyModule", "return 3;");

            // Create two resolvers with the same module name
            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent, sameModuleWithContent);
            var workspace = await workspaceProvider.CreateWorkspaceFromAllKnownModulesAsync();

            var module = AssertSuccessAndGetFirstModule(workspace);

            // We should have the first module that contains 2 specs
            XAssert.AreEqual(2, module.Specs.Count);
        }

        [Theory]
        [InlineData("import")]
        [InlineData("export")]
        public async Task ClosureContainsAllElements(string importOrExport)
        {
            var module1 = ModuleDescriptor.CreateForTesting("Module1");
            var module2 = ModuleDescriptor.CreateForTesting("Module2");
            var module3 = ModuleDescriptor.CreateForTesting("Module3");

            var moduleWithContent = CreateEmptyContent()
                .AddContent(module1, importOrExport + " * from \"Module2\";")
                .AddContent(module2, importOrExport + " * from \"Module3\";")
                .AddContent(module3, "return 3;")
                .AddContent("Module4", "return 4;");

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent);
            var workspace = await workspaceProvider.CreateWorkspaceFromModuleAsync(module1);

            var modules = AssertSuccessAndGetAllModules(workspace);

            // We should have module 1, 2 and 3. Not 4.
            XAssert.AreEqual(3, modules.Count);
            var names = modules.Select(module => module.Descriptor);
            XAssert.AreEqual(3, names.Intersect(new[] { module1, module2, module3 }).Count());
        }

        [Fact]
        public async Task ClosureCanBeComputedWhenCyclesArePresent()
        {
            var moduleWithContent =
                CreateEmptyContent()
                    .AddContent("Module1", "import * from \"Module2\";")
                    .AddContent("Module2", "import * from \"Module3\";")
                    .AddContent("Module3", "import * from \"Module1\";");

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent);
            var workspace = await workspaceProvider.CreateWorkspaceFromAllKnownModulesAsync();

            var modules = AssertSuccessAndGetAllModules(workspace);

            // We should have module 1, 2 and 3.
            XAssert.AreEqual(3, modules.Count);
        }

        [Fact]
        public async Task ClosureContainsAllElementsAcrossResolvers()
        {
            var moduleWithContent = CreateEmptyContent()
                .AddContent("Module1", "import * from \"Module2\";");

            var anotherModuleWithContent = CreateEmptyContent()
                .AddContent("Module2", "import * from \"Module3\";")
                .AddContent("Module3", "return 3;");

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent, anotherModuleWithContent);
            var workspace = await workspaceProvider.CreateWorkspaceFromAllKnownModulesAsync();

            // We should have module 1, 2 and 3.
            XAssert.AreEqual(3, AssertSuccessAndGetAllModules(workspace).Count);
        }

        [Fact]
        public async Task ResolverNotFoundIsReportedWhenComputingIncompleteClosure()
        {
            var moduleWithContent = CreateEmptyContent().AddContent("Module1", "import * from \"Module2\";");

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent);
            var workspace = await workspaceProvider.CreateWorkspaceFromAllKnownModulesAsync();

            XAssert.IsTrue(AssertSingleFailureAndGetIt(workspace) is ResolverNotFoundForModuleNameFailure);
        }

        [Fact]
        public async Task ParsingFailureIsReportedOnBadShapedSpec()
        {
            var moduleWithContent = CreateEmptyContent()
                .AddContent("MyModule", "This is not a valid DScript statement;");

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent);
            var workspace = await workspaceProvider.CreateWorkspaceFromAllKnownModulesAsync();

            // There should be a parsing failure
            XAssert.IsTrue(AssertSingleFailureAndGetIt(workspace) is ParsingFailure);
        }

        [Fact]
        public async Task ResolverNotFoundIsReportedForUnknownStartingSpec()
        {
            var myModule = ModuleDescriptor.CreateForTesting("MyModule");

            var moduleWithContent = CreateEmptyContent()
                .AddContent(myModule, "return 1;");

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent);

            // No resolver knows about a spec '42.dsc'
            var path = moduleWithContent.RootDir.Combine(PathTable, "42.dsc");
            var workspace = await workspaceProvider.CreateWorkspaceFromSpecAsync(path);

            XAssert.IsTrue(AssertSingleFailureAndGetIt(workspace) is ResolverNotFoundForPathFailure);
        }

        [Fact]
        public async Task ResolverNotFoundIsReportedForUnknownStartingModule()
        {
            var myModule = ModuleDescriptor.CreateForTesting("MyModule");

            var moduleWithContent = CreateEmptyContent()
                .AddContent(myModule, "return 1;");

            var workspaceProvider = CreateWorkspaceProviderFromContent(false, moduleWithContent);

            // No resolver knows about this module
            var module = ModuleDescriptor.CreateForTesting("UnknownModule");
            var workspace = await workspaceProvider.CreateWorkspaceFromModuleAsync(module);

            XAssert.IsTrue(AssertSingleFailureAndGetIt(workspace) is ResolverNotFoundForModuleDescriptorFailure);
        }
    }
}
