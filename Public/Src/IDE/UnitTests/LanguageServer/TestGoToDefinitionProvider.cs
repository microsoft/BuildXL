// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.VisualStudio.LanguageServer.Protocol;
using System.Linq;
using System.Threading;
using BuildXL.Ide.LanguageServer.Providers;
using BuildXL.Ide.LanguageServer.UnitTests.Helpers;
using Xunit;

namespace BuildXL.Ide.LanguageServer.UnitTests
{
    public class TestGoToDefinitionProvider : IClassFixture<WorkspaceLoaderTestFixture>
    {
        private readonly WorkspaceLoaderTestFixture m_fixture;

        public TestGoToDefinitionProvider(WorkspaceLoaderTestFixture fixture)
        {
            m_fixture = fixture;
        }

        [Fact]
        public void GoToDefinitionForFileLiteralsShouldWork()
        {
            var goToDefinitionProvider = new GotoDefinitionProvider(m_fixture.ProviderContext);

            var result = goToDefinitionProvider.GetDefinitionAtPosition(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 1 - 1,
                                   Character = 30 - 1,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"module\goToDefinitions.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var definitions = result.SuccessValue.Left.ToArray();

            Assert.Equal(1, definitions.Length);

            Assert.Contains("module/goToDefinitions.bxt", definitions[0].Uri.ToString());
        }

        [Fact]
        public void GoToDefinitionOnPropertyAssignmentShouldGoToInterfaceDeclaration()
        {
            var goToDefinitionProvider = new GotoDefinitionProvider(m_fixture.ProviderContext);

            var result = goToDefinitionProvider.GetDefinitionAtPosition(
                new TextDocumentPositionParams()
                {
                    // "con{caret}tents: [ ...]
                    Position = new Position()
                               {
                                   Line = 12 - 1,
                                   Character = 8,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"project\project.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var definitions = result.SuccessValue.Left.ToArray();

            Assert.Equal(1, definitions.Length);

            Assert.Contains("lib.bxt", definitions[0].Uri.ToString());
        }
    }
}
