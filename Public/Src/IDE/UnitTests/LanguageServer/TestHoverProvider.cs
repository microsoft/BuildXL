// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Ide.LanguageServer.Providers;
using BuildXL.Ide.LanguageServer.UnitTests.Helpers;
using LanguageServer.Json;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Xunit;

namespace BuildXL.Ide.LanguageServer.UnitTests
{
    public class TestHoverProvider : IClassFixture<WorkspaceLoaderTestFixture>
    {
        private readonly WorkspaceLoaderTestFixture m_fixture;

        public TestHoverProvider(WorkspaceLoaderTestFixture fixture)
        {
            m_fixture = fixture;
        }

        private void TestHover(int line, int character, params string[] expectedResults)
        {
            var hoverProvider = new HoverProvider(m_fixture.ProviderContext);

            var result = hoverProvider.Hover(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                    {
                        Line = line - 1,
                        Character = character- 1,
                    },
                    TextDocument = new TextDocumentIdentifier()
                    {
                        Uri = m_fixture.GetChildUri(@"module\hoverTests.bxt").ToString()
                    }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            // Hover provider could return null.

            var enumResults = (result.SuccessValue?.Contents ?? new object[] {}) .GetEnumerator();
            var enumExpectedContents = expectedResults.GetEnumerator();
            while (enumResults.MoveNext())
            {
                Assert.True(enumResults.Current is StringOrObject<MarkedString>);
                Assert.True(enumExpectedContents.MoveNext());
                Assert.Equal(enumExpectedContents.Current, (enumResults.Current as StringOrObject<MarkedString>).Right.Value);
            }
        }

        [Fact]
        public void RelativePathLiteralHover()
        {
            // Should be empty
            TestHover(38, 32);
        }

        [Fact]
        public void InterfaceDeclarationHover()
        {
            TestHover(5, 10, "boolProperty : boolean", "Test bool property.");
        }

        [Fact]
        public void InterfacePropertyHover()
        {
            TestHover(9, 10, "boolProperty : boolean");
        }

        [Fact]
        public void StringLiteralTypeHover()
        {
            TestHover(15, 10, "StringLiteralType : (\"A\" | \"B\" | \"C\")", "My string literal type.");
        }

        [Fact]
        public void StringLiteralTypeUsageHover()
        {
            TestHover(17, 10, "testStringLiteralType : (\"A\" | \"B\" | \"C\")");
        }

        [Fact]
        public void TestUnionHover()
        {
            TestHover(19, 10, "testUnionHover : (\"A\" | \"B\" | \"C\" | TestInterface)");
        }

        [Fact]
        public void TestFunctionUsageHover()
        {
            TestHover(24, 20, "function testFunctionHover(argOne: StringLiteralType, argTwo: TestInterface) : boolean", "My test function.");
        }

        [Fact]
        public void TestFunctionReturnHover()
        {
            TestHover(27, 16, "testFunctionUseHover : boolean");
        }

        [Fact]
        public void TestMapResult()
        {
            TestHover(29, 13, "testMapResult : Array<string>");
        }

        [Fact]
        public void TestEnumHover()
        {
            TestHover(31, 16, "const enum TestOne {\r\n    One,\r\n    Two,\r\n    Three,\r\n}");
        }

        [Fact]
        public void TestEnumUsageHover()
        {
            TestHover(35, 14, "testEnumUsage : TestOne");
        }
    }
}
