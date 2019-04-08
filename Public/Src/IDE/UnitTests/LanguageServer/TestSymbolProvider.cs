// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Ide.LanguageServer.Providers;
using BuildXL.Ide.LanguageServer.UnitTests.Helpers;
using Xunit;

using Microsoft.VisualStudio.LanguageServer.Protocol;


namespace BuildXL.Ide.LanguageServer.UnitTests
{
    public class SymbolProviderTest : IClassFixture<WorkspaceLoaderTestFixture>
    {
        private readonly WorkspaceLoaderTestFixture m_fixture;

        public SymbolProviderTest(WorkspaceLoaderTestFixture fixture)
        {
            m_fixture = fixture;
        }

        [Fact]
        public void TestGettingSymbolsFromModuleDeclaration()
        {
            var symbolProvider = new SymbolProvider(m_fixture.ProviderContext);

            var documentSymbolParams = new DocumentSymbolParams()
            {
                TextDocument = new TextDocumentIdentifier()
                {
                    Uri = m_fixture.GetChildUri(@"project\project.bxt").ToString()
                }                
            };

            var documentSymbolsResult = symbolProvider.DocumentSymbols(documentSymbolParams, CancellationToken.None);

            Assert.True(documentSymbolsResult.IsSuccess);

            var symbols = documentSymbolsResult.SuccessValue
                .OrderBy(symbol => symbol.Name)
                .ToArray();

            Assert.Equal(
                new SymbolInformation[]
                { 
                    new SymbolInformation() { Name = "A.B.C.a", Kind = SymbolKind.Constant },
                    new SymbolInformation() { Name = "A.B.C.b", Kind = SymbolKind.Constant },
                    new SymbolInformation() { Name = "callingAlias", Kind = SymbolKind.Variable },
                    new SymbolInformation() { Name = "callingDeployDirectly", Kind = SymbolKind.Variable },
                    new SymbolInformation() { Name = "completionForGenerics1", Kind = SymbolKind.Variable },
                    new SymbolInformation() { Name = "completionForGenerics2", Kind = SymbolKind.Variable },
                    new SymbolInformation() { Name = "IFoo", Kind = SymbolKind.Interface },
                    new SymbolInformation() { Name = "qualifier", Kind = SymbolKind.Variable },
                    new SymbolInformation() { Name = "value", Kind = SymbolKind.Variable },
                    new SymbolInformation() { Name = "value2", Kind = SymbolKind.Variable },
                    new SymbolInformation() { Name = "withQualifier", Kind = SymbolKind.Function},
                }, symbols, s_comparer);
        }

        private static readonly IEqualityComparer<SymbolInformation> s_comparer = new SymbolInformationComparer();
    }

    public class SymbolInformationComparer : IEqualityComparer<SymbolInformation>
    {
        public bool Equals(SymbolInformation x, SymbolInformation y)
        {
            return x.Name == y.Name
                && x.Kind == y.Kind;
        }

        public int GetHashCode(SymbolInformation obj)
        {
            return obj.Name.GetHashCode();
        }
    }
}
