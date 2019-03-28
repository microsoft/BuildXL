// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using Xunit;

using Microsoft.VisualStudio.LanguageServer.Protocol;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using BuildXL.Ide.LanguageServer.Completion;
using BuildXL.Ide.LanguageServer.UnitTests.Helpers;

namespace BuildXL.Ide.LanguageServer.UnitTests
{
    public class TestAutoCompleteProvider : IClassFixture<WorkspaceLoaderTestFixture>
    {
        private readonly WorkspaceLoaderTestFixture m_fixture;

        public TestAutoCompleteProvider(WorkspaceLoaderTestFixture fixture)
        {
            m_fixture = fixture;
        }

        [Fact]
        public void CompletionWithinObjectLiteral()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 24 - 1,
                                   Character = 5 - 1,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"project\project.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(2, completionItems.Length);

            Assert.Equal("a", completionItems[0].InsertText);
            Assert.Equal(CompletionItemKind.Property, completionItems[0].Kind);
            Assert.Equal("doc string for a", completionItems[0].Documentation);
            Assert.Equal("a?: string", completionItems[0].Detail);

            Assert.Equal("b", completionItems[1].InsertText);
            Assert.Equal(CompletionItemKind.Property, completionItems[1].Kind);
            Assert.Equal("doc string for b", completionItems[1].Documentation);
            Assert.Equal("b?: DeployableItem", completionItems[1].Detail);
        }

        [Fact]
        public void AutoCompleteWithObjectIteralAssignedToVariable()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 94- 1,
                                   Character = 42,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"module/lib.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(1, completionItems.Length);

            Assert.Equal("interfaceAProperty", completionItems[0].Label);
        }

        [Fact]
        public void AutoCompleteWithNestedObjectIteralAssignedToVariable()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 99 - 1,
                                   Character = 32,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"module/lib.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(1, completionItems.Length);

            Assert.Equal("interfaceAProperty", completionItems[0].Label);
        }

        [Fact]
        public void AutoCompleteOnImportShouldProvideOnlyOneModule()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 1 - 1,
                                   Character = 26,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"project\project.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(1, completionItems.Length);

            // TODO: Change to just BuildXL.DScript check after April 15, 2019 when deployed bits have updated.
            Assert.True(completionItems[0].Label == "BuildXL.DScript.LanguageServer.UnitTests.Data.Module" ||
                        completionItems[0].Label == "BuildXLScript.LanguageServer.UnitTests.Data.Module");
        }

        [Fact]
        public void AutoCompleteWithObjectLiteralAssignedToVariableWithTypeAssertion()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 104 - 1,
                                   Character = 58,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"module/lib.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(1, completionItems.Length);

            Assert.Equal("interfaceAProperty", completionItems[0].Label);
        }

        [Fact]
        public void AutoCompleteWithNestedObjectLiteralAssignedToVariableWithTypeAssertion()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 109 - 1,
                                   Character = 31,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"module/lib.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(1, completionItems.Length);

            Assert.Equal("interfaceAProperty", completionItems[0].Label);
        }

        [Fact]
        public void AutoCompleteWithFunctionThatReturnsNestedObjectLiteral()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 121 - 1,
                                   Character = 36,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"module/lib.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(1, completionItems.Length);

            Assert.Equal("interfaceAProperty", completionItems[0].Label);
        }

        [Fact]
        public void AutoCompleteFunctionCallThatContainsNestedObjectLiteral()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 130 - 1,
                                   Character = 33,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"module/lib.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(1, completionItems.Length);

            Assert.Equal("interfaceAProperty", completionItems[0].Label);
        }

        [Fact]
        public void AutoCompleteFunctionCallThroughReExport()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 3 - 1,
                                   Character = 40,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"module\testExportedFunction.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(1, completionItems.Length);

            Assert.Equal("myFunctionProperty", completionItems[0].Label);
        }

        [Fact]
        public void AutoCompleteWithDot()
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            var result = autoCompleteProvider.Completion(
                new TextDocumentPositionParams()
                {
                    Position = new Position()
                               {
                                   Line = 19 - 1,
                                   Character = 96,
                               },
                    TextDocument = new TextDocumentIdentifier()
                                   {
                                       Uri = m_fixture.GetChildUri(@"project\project.bxt").ToString()
                                   }
                }, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var completionItems = result.SuccessValue.Left
                .OrderBy(ci => ci.Label)
                .ToArray();

            Assert.Equal(2, completionItems.Length);

            Assert.Equal("deploy", completionItems[0].Label);
            Assert.Equal(CompletionItemKind.Function, completionItems[0].Kind);
            Assert.Equal(
                @"Callback for when deployments will be FlattenedResult
 @param item - The item that is deployable. Think of this as the 'this' pointer which is not accessable from interface implementations.
 @param targetFolder - The folder to place this deployable item into
 @param onDuplicate - The error handler for duplicate files
 @param currentResult - The current flattened result to add the extra flattened files to 
 @return - The updated flattened result.", completionItems[0].Documentation);
            Assert.Equal("deploy(item: Object, targetFolder: RelativePath, reportDuplicate: ReportDuplicateDeploymentError, currentResult: FlattenedResult, deploymentOptions?: Object)", completionItems[0].Detail);

            Assert.Equal("name", completionItems[1].Label);
            Assert.Equal("name", completionItems[1].InsertText);
            Assert.Equal(CompletionItemKind.Property, completionItems[1].Kind);
            Assert.Equal(string.Empty, completionItems[1].Documentation);
            Assert.Equal("name: string", completionItems[1].Detail);
        }

        public class AutoCompleteLabelTestInformation
        {
            public int line;
            public int character;
            public string filePath;
            public List<string> expectedLabels;
        };

        private string TestItemToString(AutoCompleteLabelTestInformation testItem)
        {
            var builder = new StringBuilder();

            builder.AppendLine("[");
            builder.Append("File: ");
            builder.Append(testItem.filePath);
            builder.AppendLine();

            builder.Append("Line: ");
            builder.Append(testItem.line);
            builder.AppendLine();

            builder.Append("Character: ");
            builder.Append(testItem.character);
            builder.AppendLine();

            builder.AppendLine("Labels:");
            foreach (var expectedLabel in testItem.expectedLabels)
            {
                builder.AppendLine("  [");
                builder.Append("    ");
                builder.Append("Label: ");
                builder.Append(expectedLabel);
                builder.AppendLine();
                builder.AppendLine("  ],");
            }
            builder.AppendLine("]");

            builder.AppendLine();
            return builder.ToString();
        }

        public string CompletionItemsLabelsToString(IEnumerable<CompletionItem> completionItems)
        {
            var builder = new StringBuilder();

            foreach (var completionItem in completionItems)
            {
                builder.AppendLine("  [");
                builder.Append("    ");
                builder.Append("Label: ");
                builder.Append(completionItem.Label);
                builder.AppendLine();
                builder.AppendLine("  ],");
            }

            builder.AppendLine();
            return builder.ToString();
        }

        private void TestAutoCompleteLabels(List<AutoCompleteLabelTestInformation> testList)
        {
            var autoCompleteProvider = new AutoCompleteProvider(m_fixture.ProviderContext);

            foreach (var test in testList)
            {
                var result = autoCompleteProvider.Completion(
                    new TextDocumentPositionParams()
                    {
                        Position = new Position()
                                   {
                                       Line = test.line - 1,
                                       Character = test.character,
                                   },
                        TextDocument = new TextDocumentIdentifier()
                                       {
                                           Uri = m_fixture.GetChildUri(test.filePath).ToString()
                                       }
                    }
                    , CancellationToken.None);

                Assert.True(result.IsSuccess);

                var completionItems = result.SuccessValue.Left.OrderBy(ci => ci.Label).ToArray();
                Assert.True(test.expectedLabels.Count == completionItems.Length, "Expected label count does not match\r\n" + "Expected: \r \n" + TestItemToString(test) + "Actual: \r\n" + CompletionItemsLabelsToString(completionItems));

                foreach (var expectedLabel in test.expectedLabels)
                {
                    var hasLabel = completionItems.Any(item => item.Label.Equals(expectedLabel));
                    Assert.True(hasLabel, "Expected labels not found\r\n" + "Expected: \r \n" + TestItemToString(test) + "Actual: \r\n" + CompletionItemsLabelsToString(completionItems));
                }
            }
        }

        [Fact]
        public void AutoCompleteWithObjectLiteralExpressions()
        {
            var expectedMergeLabels = new List<string>
            {
                "get",
                "keys",
                "merge",
                "customMerge",
                "withCustomMerge",
                "override",
                "overrideKey",
                "objectLiteralTypePropertyOne",
                "objectLiteralTypePropertyTwo",
                "toString",
            };

            TestAutoCompleteLabels(new List<AutoCompleteLabelTestInformation>
            {
                new AutoCompleteLabelTestInformation
                {
                    line = 18,
                    character = 40,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = new List<string>
                    {
                        "objectLiteralTypePropertyTwo"
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    line = 22,
                    character = 40,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = new List<string>
                    {
                        "objectLiteralTypePropertyTwo",
                        "derivedObjectLiteralTypePropertyOne",
                        "derivedObjectLiteralTypePropertyTwo",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    line = 26,
                    character = 40,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = new List<string>
                    {
                        "objectLiteralTypePropertyTwo",
                        "secondObjectLiteralTypePropertyOne",
                        "secondObjectLiteralTypePropertyTwo",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    line = 30,
                    character = 40,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = new List<string>
                    {
                        "objectLiteralTypePropertyTwo",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    line = 34,
                    character = 40,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = new List<string>
                    {
                        "objectLiteralTypePropertyTwo",
                        "derivedObjectLiteralTypePropertyOne",
                        "derivedObjectLiteralTypePropertyTwo",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    line = 38,
                    character = 40,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = new List<string>
                    {
                        "objectLiteralTypePropertyTwo",
                        "secondObjectLiteralTypePropertyOne",
                        "secondObjectLiteralTypePropertyTwo",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    line = 55,
                    character = 47,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = new List<string>
                    {
                        "objectLiteralTypePropertyOne",
                        "objectLiteralTypePropertyTwo",
                        "secondObjectLiteralTypePropertyOne",
                        "secondObjectLiteralTypePropertyTwo",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    line = 67,
                    character = 42,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = expectedMergeLabels,
                },
                // Test override
                new AutoCompleteLabelTestInformation
                {
                    line = 72,
                    character = 45,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = expectedMergeLabels,
                },
                // Test overrideKey
                new AutoCompleteLabelTestInformation
                {
                    line = 77,
                    character = 80,
                    filePath = @"module\ObjectLiteralExpressions.bxt",
                    expectedLabels = expectedMergeLabels,
                },
            });
        }

        [Fact]
        public void AutoCompleteWithStringLiterals()
        {
            TestAutoCompleteLabels(new List<AutoCompleteLabelTestInformation>
            {
                new AutoCompleteLabelTestInformation
                {
                    //     myProperty: "{caret}"
                    line = 12,
                    character = 18,
                    filePath = @"module\stringLiteral.bxt",
                    expectedLabels = new List<string>
                    {
                        "OptionA",
                        "OptionB",
                        "OptionC",
                        "",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    //         case "{caret}":
                    line = 19,
                    character = 15,
                    filePath = @"module\stringLiteral.bxt",
                    expectedLabels = new List<string>
                    {
                        "A",
                        "B",
                        "",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    //     if (argOne === "{caret}")
                    line = 23,
                    character = 21,
                    filePath = @"module\stringLiteral.bxt",
                    expectedLabels = new List<string>
                    {
                        "A",
                        "B",
                        "",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    //     else if ("{caret}" === argTwo.myProperty)
                    line = 27,
                    character = 15,
                    filePath = @"module\stringLiteral.bxt",
                    expectedLabels = new List<string>
                    {
                        "OptionA",
                        "OptionB",
                        "OptionC",
                        "",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    // const myTestFuncCall = myFunc("{caret}", { myProperty: ""});
                    line = 33,
                    character = 32,
                    filePath = @"module\stringLiteral.bxt",
                    expectedLabels = new List<string>
                    {
                        "A",
                        "B",
                        "",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    //         case "{caret}":
                    line = 43,
                    character = 15,
                    filePath = @"module\stringLiteral.bxt",
                    expectedLabels = new List<string>
                    {
                        "A",
                        "B",
                        "",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    //     if (x === "{caret}")
                    line = 47,
                    character = 16,
                    filePath = @"module\stringLiteral.bxt",
                    expectedLabels = new List<string>
                    {
                        "A",
                        "B",
                        "",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    //     else if ("{caret}" === x)
                    line = 51,
                    character = 15,
                    filePath = @"module\stringLiteral.bxt",
                    expectedLabels = new List<string>
                    {
                        "A",
                        "B",
                        "",
                    }
                },
            });
        }

        [Fact]
        public void AutoCompleteWithReturnStatement()
        {
            TestAutoCompleteLabels(new List<AutoCompleteLabelTestInformation>
            {
                new AutoCompleteLabelTestInformation
                {
                    line = 17,
                    character = 13,
                    filePath = @"module\returnStatement.bxt",
                    expectedLabels = new List<string>
                    {
                        "objectLiteralTypePropertyTwo"
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    line = 23,
                    character = 13,
                    filePath = @"module\returnStatement.bxt",
                    expectedLabels = new List<string>
                    {
                        "derivedObjectLiteralTypePropertyOne",
                        "derivedObjectLiteralTypePropertyTwo",
                        "objectLiteralTypePropertyTwo",
                    }
                },
                new AutoCompleteLabelTestInformation
                {
                    line = 29,
                    character = 13,
                    filePath = @"module\returnStatement.bxt",
                    expectedLabels = new List<string>
                    {
                        "objectLiteralTypePropertyTwo",
                        "secondObjectLiteralTypePropertyOne",
                        "secondObjectLiteralTypePropertyTwo",
                    }
                },
            });
        }

        [Fact]
        public void AutoCompleteInsideCallExpression()
        {
            TestAutoCompleteLabels(new List<AutoCompleteLabelTestInformation>
            {
                new AutoCompleteLabelTestInformation
                {
                    line = 8,
                    character = 40,
                    filePath = @"module\callExpression.bxt",
                    expectedLabels = new List<string>
                    {
                        "someProperty"
                    }
                },
            });
        }
    }
}
