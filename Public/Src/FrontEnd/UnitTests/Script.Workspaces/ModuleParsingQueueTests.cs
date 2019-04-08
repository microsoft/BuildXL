// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Workspaces.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Workspaces.Utilities;
using TypeScript.Net.Types;
using Xunit;

namespace Test.DScript.Workspaces
{
    public class ModuleParsingQueueTests : WorkspaceTestBase
    {
        [Fact]
        public async Task ExceptionsAreProperlyPropagated()
        {
            var moduleName = ModuleDescriptor.CreateForTesting("MyModule");
            var moduleWithContent = CreateEmptyContent().AddContent(moduleName, "return 1;");

            var queue = CreateParsingQueueFromContent(new[] { moduleWithContent }, new NotImplementedFileSystem());
            var modules = new[] { GetModuleDefinitionFromContent(moduleName, moduleWithContent) };

            try
            {
                var parsingResult = await queue.ProcessAsync(modules);
                XAssert.Fail("Should never get here");
            }
            catch (NotImplementedException)
            {
            }
        }

        [Fact]
        public async Task ParsingCompletesWhenCancelOnFirstFailureIsDisabled()
        {
            var module1 = ModuleDescriptor.CreateForTesting("Module1");
            var module2 = ModuleDescriptor.CreateForTesting("Module2");

            var moduleWithContent = CreateEmptyContent()
                .AddContent(module1, "This spec is not DScript;")
                .AddContent(module2, "return 2;");

            var queue = CreateParsingQueueFromContent(new[] { moduleWithContent });

            var modules = new[]
            {
                GetModuleDefinitionFromContent(module1, moduleWithContent),
                GetModuleDefinitionFromContent(module2, moduleWithContent)
            };

            var parsingResult = await queue.ProcessAsync(modules);
            // There should be one failure
            XAssert.IsTrue(AssertSingleFailureAndGetIt(parsingResult) is ParsingFailure);

            // But still there should be 2 modules in the result and parsing should not be cancelled
            XAssert.AreEqual(2, parsingResult.SpecModules.Count);
        }

        [Fact]
        public async Task AllSpecsAreAddedToTheModuleWhenCancelOnFirstFailureIsDisabled()
        {
            var module1 = ModuleDescriptor.CreateForTesting("Module1");

            var moduleWithContent = CreateEmptyContent()
                .AddContent(
                    module1,
                    "This spec is not DScript;",
                    "This spec is not DScript either;");

            var queue = CreateParsingQueueFromContent(new[] { moduleWithContent });

            var modules = new[]
            {
                GetModuleDefinitionFromContent(module1, moduleWithContent),
            };

            var parsingResult = await queue.ProcessAsync(modules);

            // There should be two specs and two failures
            XAssert.AreEqual(2, parsingResult.SpecCount);
            XAssert.AreEqual(2, parsingResult.Failures.Count);
        }

        [Fact]
        public async Task SpecsAreBoundEvenWithErrorsWhenCancelOnFirstFailureIsDisabled()
        {
            var module1 = ModuleDescriptor.CreateForTesting("Module1");

            var moduleWithContent = CreateEmptyContent()
                .AddContent(module1, "This spec is not DScript;");

            var queue = CreateParsingQueueFromContent(new[] { moduleWithContent });

            var modules = new[]
            {
                GetModuleDefinitionFromContent(module1, moduleWithContent),
            };

            var parsingResult = await queue.ProcessAsync(modules);

            // There should be one spec and one failure
            XAssert.AreEqual(1, parsingResult.SpecCount);
            XAssert.AreEqual(1, parsingResult.Failures.Count);

            // The spec should be bound
            var sourceFile = parsingResult.SpecSources.First().Value.SourceFile;
            XAssert.AreEqual(SourceFileState.Bound, sourceFile.State);
        }

        [Fact]
        public async Task AModuleWithNoSpecsDoesNotStallTheQueue()
        {
            var module1 = ModuleDescriptor.CreateForTesting("Module1");
            var moduleWithContent = CreateEmptyContent()
                .AddContent(module1);

            var queue = CreateParsingQueueFromContent(new[] { moduleWithContent }, fileSystem: new NotImplementedFileSystem());

            var modules = new[] { GetModuleDefinitionFromContent(module1, moduleWithContent) };

            var parsingResult = await queue.ProcessAsync(modules);

            XAssert.IsTrue(parsingResult.Succeeded);
        }
    }
}
