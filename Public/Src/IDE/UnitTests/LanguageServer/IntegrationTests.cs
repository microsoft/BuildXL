// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Ide.JsonRpc;
using BuildXL.Ide.LanguageServer.UnitTests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Ide.LanguageServer.UnitTests
{
    public class IntegrationTests
    {
        private ITestOutputHelper Output { get; }

        static IntegrationTests()
        {
           AppDomain.CurrentDomain.AssemblyResolve += AssemblyLoaderHelper.Newtonsoft11DomainAssemblyResolve;
        }

        public IntegrationTests(ITestOutputHelper output)
        {
            Output = output;
        }

        [Fact]
        public void WorkspaceLoadingCompletes()
        {
            var messages = IntegrationTestHelper
                .CreateApp(Output)
                .Invoke("exit")
                .WorkspaceLoadingMessages;

            Assert.True(messages.Any(message => message.Status == WorkspaceLoadingState.Success));
        }

        [Fact]
        public void DiagnosticsAreProvidedOnMalformedSpec()
        {
            var lastDiagnostic = IntegrationTestHelper
                .CreateApp(Output)
                .NotifyDocumentOpened(
@"const x = 42; 
const y = ")
                .PublishDiagnostics
                .Last();

            // Two diagnostics are expected, the expression after the '=' is missing and the final semicolon as well
            Assert.Contains(lastDiagnostic.Diagnostics, diag => diag.Message == Errors.Semicolon_expected.Message);
            Assert.Contains(lastDiagnostic.Diagnostics, diag => diag.Message == Errors.Expression_expected.Message);
        }

        [Fact]
        public void ReferenceTheNewExposedVariableFromTheOldDocumentShouldWork()
        {
            var spec1 = IntegrationTestHelper.CreateDocument(
                "spec1.dsc", "export const x = 42;");

            var spec2 = IntegrationTestHelper.CreateDocument(
                "spec2.dsc", "export const y = 42;");
            
            // Create an app with only the first spec.
            var app = IntegrationTestHelper.CreateApp(Output, spec1);

            var diagnostics = app
                // Open the second spec
                .NotifyDocumentOpened(spec2)
                // Then changing the first spec to use the value from the second spec.
                .NotifyDocumentOpened(
                IntegrationTestHelper.CreateDocument(
                    "spec1.dsc", "export const x = y;"))
                .PublishDiagnostics;

            Assert.Empty(diagnostics.Where(d => d.Diagnostics.Length != 0));
        }

        [Fact]
        public void SimpleCompletion()
        {
            var app = IntegrationTestHelper.CreateApp(Output);

            // Position (12,2) is right after 'x.'
            var item = app.GetDocumentItemFromContent(
@"interface I { a : string};
const x : I = undefined;
const y = x.");

            var completionItems = app
                .NotifyDocumentOpened(item)
                .Invoke(
                    "textDocument/completion", 
                    new TextDocumentPositionParams
                    {
                        Position = new Position { Character = 12, Line = 2 },
                        TextDocument = new TextDocumentIdentifier { Uri = item.Uri }
                    })
                .GetLastInvocationResult<List<CompletionItem>>();

            // One single completion item is expected, with insert text 'a'
            Assert.Equal(1, completionItems.Count);
            Assert.Equal("a", completionItems.First().InsertText);
        }

        [Fact]
        public void CompletionOnInterfaceMerging()
        {
            var app = IntegrationTestHelper.CreateApp(Output);

            // Position (12,2) is right after 'x.'
            var item = app.GetDocumentItemFromContent(
                @"export interface I { a : string};
const x : I = undefined;
const y = x.");

            var completionRequest = new TextDocumentPositionParams
                                    {
                                        Position = new Position {Character = 12, Line = 2},
                                        TextDocument = new TextDocumentIdentifier {Uri = item.Uri}
                                    };

            // We call completion once. Then add another file that extends the interface and call completion again
            app
                .NotifyDocumentOpened(item)
                .Invoke<TextDocumentPositionParams, List<CompletionItem>>(
                    "textDocument/completion", completionRequest, out var completionList1)
                .NotifyDocumentOpened(@"export interface I { b: string }")
                .Invoke<TextDocumentPositionParams, List<CompletionItem>>(
                    "textDocument/completion", completionRequest, out var completionList2);

            // One single completion item is expected first, with insert text 'a'
            Assert.Equal(1, completionList1.Count);
            Assert.Equal("a", completionList1.First().InsertText);

            // Two completion items are expected after extending the interface, with insert text 'a' and 'b'
            Assert.Equal(2, completionList2.Count);
            Assert.Contains(completionList2, completionItem => completionItem.InsertText == "a");
            Assert.Contains(completionList2, completionItem => completionItem.InsertText == "b");
        }


        private IntegrationTestHelper SetupForProjectManagementTests()
        {
            var app = IntegrationTestHelper.CreateApp(Output);

            var sourceFileConfigurations = new List<AddSourceFileConfiguration>
            {
                new AddSourceFileConfiguration
                {
                    FunctionName = "build",
                    PropertyName = "sources",
                    ArgumentTypeName = "Arguments",
                    ArgumentPosition = 0,
                    ArgumentTypeModuleName = "__Config__"
                }
            };

            var addSourceConfiguration = new AddSourceFileConfigurationParams
            {
                Configurations = sourceFileConfigurations.ToArray()
            };

            app.SendNotification("dscript/sourceFileConfiguration", addSourceConfiguration);

            app.NotifyDocumentOpened(
@"
namespace StaticLibrary {
     export interface Arguments {
         sources: File[];
     }

     export interface BuildResult {        
     }

     export function build(args: Arguments): BuildResult {
        return undefined;
     }
}");

            return app;

        }

        [Fact]
        public void AddSourceToNonEmptyList()
        {
            var app = SetupForProjectManagementTests();
            var addSourcesCode = app.GetDocumentItemFromContent(
@"export const x = StaticLibrary.build({
      sources: [f`foo.cpp`]
    });");

            var addSourceFileParams = new AddSourceFileToProjectParams
            {
                RelativeSourceFilePath = "bar.cpp",
                ProjectSpecFileName = addSourcesCode.Uri
            };

            app.NotifyDocumentOpened(addSourcesCode).
                Invoke<AddSourceFileToProjectParams, List<TextEdit>>("dscript/addSourceFileToProject", addSourceFileParams, out var results);

            Assert.Equal(1, results.Count);
            Assert.Equal(
@"export const x = StaticLibrary.build({sources: [
        f`foo.cpp`,
        f`bar.cpp`,
    ]});", results[0].NewText);
        }

        [Fact]
        public void AddSourceToEmptyList()
        {
            var app = SetupForProjectManagementTests();
            var addSourcesCode = app.GetDocumentItemFromContent(
@"
export const x = StaticLibrary.build({
      sources: []
    });");

            var addSourceFileParams = new AddSourceFileToProjectParams
            {
                RelativeSourceFilePath = "foo.cpp",
                ProjectSpecFileName = addSourcesCode.Uri
            };

            app.NotifyDocumentOpened(addSourcesCode).
                Invoke<AddSourceFileToProjectParams, List<TextEdit>>("dscript/addSourceFileToProject", addSourceFileParams, out var results);

            Assert.Equal(1, results.Count);
            Assert.Equal(@"export const x = StaticLibrary.build({sources: [f`foo.cpp`]});", results[0].NewText);
        }
    }
}
