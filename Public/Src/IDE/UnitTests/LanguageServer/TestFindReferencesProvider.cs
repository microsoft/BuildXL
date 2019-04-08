// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using LanguageServer;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using System.Linq;
using BuildXL.Ide.JsonRpc;
using BuildXL.Ide.LanguageServer.Providers;
using BuildXL.Ide.LanguageServer.UnitTests.Helpers;
using Xunit;

namespace BuildXL.Ide.LanguageServer.UnitTests
{
    public class TestFindReferencesProvider : IClassFixture<WorkspaceLoaderTestFixture>
    {
        private readonly WorkspaceLoaderTestFixture m_fixture;

        public TestFindReferencesProvider(WorkspaceLoaderTestFixture fixture)
        {
            m_fixture = fixture;
        }
        
        [Fact]
        public void FindReferencesForStringLiteralType()
        {
            var findReferencesProvider = CreateProvider(m_fixture.ProviderContext);

            var result = findReferencesProvider.GetReferencesAtPosition(
                @"module\stringLiteral.bxt(1,20)".ToParams(m_fixture));

            AssertReferences(result, "stringLiteral.bxt", 1, 57);
        }

        [Fact]
        public void FindReferencesForStringLiteral()
        {
            var findReferencesProvider = CreateProvider(m_fixture.ProviderContext);

            var result = findReferencesProvider.GetReferencesAtPosition(
                // const x = 'Opt{caret}ionA';
                @"module\stringLiteral.bxt(57,15)".ToParams(m_fixture));

            AssertReferences(result, "stringLiteral.bxt", 1, 57);
        }

        [Fact]
        public void FindReferencesForFile()
        {
            var findReferencesProvider = CreateProvider(m_fixture.ProviderContext);

            var result = findReferencesProvider.GetReferencesAtPosition(
                // const f1 = f`foo.dsc`;
                @"module\fileLikeLiterals.bxt(1,16)".ToParams(m_fixture));

            AssertReferences(result, "fileLikeLiterals.bxt", 1, 2, 5, 6);
        }

        [Fact]
        public void FindReferencesForDirectory()
        {
            var findReferencesProvider = CreateProvider(m_fixture.ProviderContext);

            var result = findReferencesProvider.GetReferencesAtPosition(
                // const p1 = p`foo.dsc`;
                @"module\fileLikeLiterals.bxt(6,16)".ToParams(m_fixture));

            AssertReferences(result, "fileLikeLiterals.bxt", 1, 2, 5, 6);
        }

        [Fact]
        public void FindReferencesForAtom()
        {
            var findReferencesProvider = CreateProvider(m_fixture.ProviderContext);

            var result = findReferencesProvider.GetReferencesAtPosition(
                // const p1 = p`foo.dsc`;
                @"module\fileLikeLiterals.bxt(8,16)".ToParams(m_fixture));

            AssertReferences(result, "fileLikeLiterals.bxt", 8, 9);
        }

        [Fact]
        public void FindReferencesForPath()
        {
            var findReferencesProvider = CreateProvider(m_fixture.ProviderContext);

            var result = findReferencesProvider.GetReferencesAtPosition(
                // const d1 = d`foo.dsc`;
                @"module\fileLikeLiterals.bxt(5,16)".ToParams(m_fixture));

            AssertReferences(result, "fileLikeLiterals.bxt", 1, 2, 5, 6);
        }

        private void AssertReferences(Result<Location[], ResponseError> findReferencesResult, string expectedFileName, params int[] expectedLines)
        {
            Assert.True(findReferencesResult.IsSuccess);

            var definitions = findReferencesResult.SuccessValue;

            Assert.Equal(expectedLines.Length, definitions.Length);
            Assert.Contains(expectedFileName, definitions[0].Uri.ToString());

            var lines = GetLines(definitions);
            // References are located at the following lines
            Assert.Equal(expectedLines, lines);
        }

        private static FindReferencesProvider CreateProvider(ProviderContext providerContext)
        {
            return new FindReferencesProvider(providerContext, new EmptyProgressReporter());
        }

        private static int[] GetLines(Location[] locations)
        {
            return locations.Select(l => l.Range.Start.Line + 1).ToArray();
        }
    }
}
