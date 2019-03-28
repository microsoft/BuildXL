// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Ide.LanguageServer.Providers;
using BuildXL.Ide.LanguageServer.UnitTests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace BuildXL.Ide.LanguageServer.UnitTests
{
    public class TestRenameProvider : IClassFixture<WorkspaceLoaderTestFixture>
    {
        private readonly WorkspaceLoaderTestFixture m_fixture;
        private readonly string m_filePath =  R("module", "renameTests.bxt");

        public TestRenameProvider(WorkspaceLoaderTestFixture fixture)
        {
            m_fixture = fixture;
        }
        
        private RenameProvider CreateRenameProvider()
        {
            FindReferencesProvider findReferencesProvider = new FindReferencesProvider(m_fixture.ProviderContext, new EmptyProgressReporter());
            return new RenameProvider(m_fixture.ProviderContext, findReferencesProvider);
        }

        public class RenameProviderTestInformation
        {
            public int Line;
            public int Character;
            public string FilePath;
            public Dictionary<string, TextEdit[]> ExpectedUriToEdits;
        };

        private void TestRename(RenameProviderTestInformation info)
        {
            var renameProvider = CreateRenameProvider();
            var edits = renameProvider.GetWorkspaceEdits(new RenameParams
                                                         {
                                                             NewName = "TestMe",
                                                             Position = new Position
                                                                        {
                                                                            Line = info.Line - 1,
                                                                            Character = info.Character,
                                                                        },
                                                             TextDocument = new TextDocumentIdentifier
                                                                            {
                                                                                Uri = m_fixture.GetChildUri(info.FilePath).ToString()
                                                                            }
                                                         }, CancellationToken.None);

            Assert.True(edits.IsSuccess);
            Assert.Equal(edits.SuccessValue.Changes.Count, info.ExpectedUriToEdits.Count);
            foreach (var expectedUriToEdit in info.ExpectedUriToEdits)
            {
                // Make sure we have a results for the expected URI
                var testUri = m_fixture.GetChildUri(expectedUriToEdit.Key);
                Assert.True(edits.SuccessValue.Changes.TryGetValue(testUri.ToString(), out var actualEdits));

                // Now verify all the edits. Our test information is one based (for sanity purposes)
                Assert.True(actualEdits.Any(actualEdit => expectedUriToEdit.Value.Any(expectedEdit =>
                    {
                        return actualEdit.Range.Start.Character == expectedEdit.Range.Start.Character - 1 &&
                                actualEdit.Range.Start.Line == expectedEdit.Range.Start.Line - 1 &&
                                actualEdit.Range.End.Character == expectedEdit.Range.End.Character - 1 &&
                                actualEdit.Range.End.Line == expectedEdit.Range.End.Line - 1;
                    }
                )));
            }
        }

        [Fact]
        public void TestMostBasicRename()
        {
            TestRename(new RenameProviderTestInformation
            {
                Line = 2,
                Character = 7,
                FilePath = m_filePath,
                ExpectedUriToEdits = new Dictionary<string, TextEdit[]>
                {
                    [m_filePath] = new TextEdit[1]
                    {
                        new TextEdit
                        {
                            Range = new Range
                            {
                                Start = new Position
                                {
                                    Line = 2,
                                    Character = 7
                                },
                                End = new Position
                                {
                                    Line = 2,
                                    Character = 15
                                }
                            }
                        }
                    }
                }
            });
        }

        [Fact]
        public void TestEnumMemberRename()
        {
            TestRename(new RenameProviderTestInformation
            {
                Line = 11,
                Character = 38,
                FilePath = m_filePath,
                ExpectedUriToEdits = new Dictionary<string, TextEdit[]>
                {
                    [m_filePath] = new TextEdit[2]
                    {
                        new TextEdit
                        {
                            Range = new Range
                            {
                                Start = new Position
                                {
                                    Line = 11,
                                    Character = 38,
                                },
                                End = new Position
                                {
                                    Line = 11,
                                    Character = 41
                                }
                            }
                        },
                        new TextEdit
                        {
                            Range = new Range
                            {
                                Start = new Position
                                {
                                    Line = 6,
                                    Character = 5,
                                },
                                End = new Position
                                {
                                    Line = 6,
                                    Character = 8
                                }
                            }
                        }
                    }
                }
            });
        }

        [Fact]
        public void TestEnumRename()
        {
            TestRename(new RenameProviderTestInformation
            {
                Line = 5,
                Character = 19,
                FilePath = m_filePath,
                ExpectedUriToEdits = new Dictionary<string, TextEdit[]>
                {
                    [m_filePath] = new TextEdit[2]
                    {
                        new TextEdit
                        {
                            Range = new Range
                            {
                                Start = new Position
                                {
                                    Line = 5,
                                    Character = 19,
                                },
                                End = new Position
                                {
                                    Line = 5,
                                    Character = 33
                                }
                            }
                        },
                        new TextEdit
                        {
                            Range = new Range
                            {
                                Start = new Position
                                {
                                    Line = 11,
                                    Character = 23,
                                },
                                End = new Position
                                {
                                    Line = 11,
                                    Character = 37
                                }
                            }
                        }
                    }
                }
            });
        }

        [Fact]
        public void TestNamespaceRename()
        {
            TestRename(new RenameProviderTestInformation
            {
                Line = 14,
                Character = 11,
                FilePath = m_filePath,
                ExpectedUriToEdits = new Dictionary<string, TextEdit[]>
                {
                    [m_filePath] = new TextEdit[2]
                    {
                        new TextEdit
                        {
                            Range = new Range
                            {
                                Start = new Position
                                {
                                    Line = 14,
                                    Character = 11,
                                },
                                End = new Position
                                {
                                    Line = 14,
                                    Character = 30
                                }
                            }
                        },
                        new TextEdit
                        {
                            Range = new Range
                            {
                                Start = new Position
                                {
                                    Line = 18,
                                    Character = 11,
                                },
                                End = new Position
                                {
                                    Line = 18,
                                    Character = 30
                                }
                            }
                        }
                    }
                }
            });
        }

        [Fact]
        public void TestNamespaceRenameCrossFile()
        {
            TestRename(new RenameProviderTestInformation
            {
                Line = 22,
                Character = 11,
                FilePath = m_filePath,
                ExpectedUriToEdits = new Dictionary<string, TextEdit[]>
                {
                    [m_filePath] = new TextEdit[1]
                    {
                        new TextEdit
                        {
                            Range = new Range
                            {
                                Start = new Position
                                {
                                    Line = 22,
                                    Character = 11,
                                },
                                End = new Position
                                {
                                    Line = 22,
                                    Character = 29
                                }
                            }
                        },
                    },
                    [R("module", "renameTestsMultiFile.bxt")] = new TextEdit[1]
                    {
                        new TextEdit
                        {
                            Range = new Range
                            {
                                Start = new Position
                                {
                                    Line = 1,
                                    Character = 11,
                                },
                                End = new Position
                                {
                                    Line = 1,
                                    Character = 29
                                }
                            }
                        }
                    }
                }
            });
        }
    }
}
