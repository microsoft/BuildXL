// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading;
using BuildXL.Ide.LanguageServer.Providers;
using BuildXL.Ide.LanguageServer.UnitTests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Xunit;

namespace BuildXL.Ide.LanguageServer.UnitTests
{
    public class TestSignatureHelpProvider : IClassFixture<WorkspaceLoaderTestFixture>
    {
        private readonly WorkspaceLoaderTestFixture m_fixture;

        public TestSignatureHelpProvider(WorkspaceLoaderTestFixture fixture)
        {
            m_fixture = fixture;
        }

        [Fact]
        public void SignatureHelpForGenericMemberFunctionShouldNotThrow()
        {
            // Bug #14880627
            var signatureHelp = new SignatureHelpProvider(m_fixture.ProviderContext);

            var result = signatureHelp.SignatureHelp(
                new TextDocumentPositionParams
                {
                    Position = new Position
                               {
                                   Line = 38 - 1,
                                   Character = 70 - 1,
                               },
                    TextDocument = new TextDocumentIdentifier
                                   {
                                       Uri = m_fixture.GetChildUri(@"project\completionForGenerics.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void SignatureHelpDirectFunction()
        {
            var signatureHelp = new SignatureHelpProvider(m_fixture.ProviderContext);

            var result = signatureHelp.SignatureHelp(
                new TextDocumentPositionParams
                {
                    Position = new Position
                               {
                                   Line = 19 - 1,
                                   Character = 71 - 1,
                               },
                    TextDocument = new TextDocumentIdentifier
                                   {
                                       Uri = m_fixture.GetChildUri(@"project\project.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            // we only have one signature so for now we're just always defaulting to the "first" one
            Assert.Equal(0, result.SuccessValue.ActiveSignature);
            
            // we're in the 'b' argument for the function with our cursor
            Assert.Equal(1, result.SuccessValue.ActiveParameter);

            var signatures = result.SuccessValue.Signatures.ToArray();
            Assert.Equal(1, signatures.Length);

            Assert.Equal("functionWithMultipleParameters(a: string, b: Definition, c: string[]) : Deployable", signatures[0].Label);
            signatures[0].Documentation = @"documentation for functionWithMultipleParameters";

            var parameters = signatures[0].Parameters.ToArray();
            Assert.Equal(3, parameters.Length);

            parameters[0].Label = "a: string";
            Assert.Null(parameters[0].Documentation);

            parameters[1].Label = "b: Definition";
            Assert.Null(parameters[1].Documentation);

            parameters[2].Label = "c: string[]";
            Assert.Null(parameters[2].Documentation);
        }
    }
}
