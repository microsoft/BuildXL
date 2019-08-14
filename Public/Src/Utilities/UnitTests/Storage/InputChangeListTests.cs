// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Storage.InputChange;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Storage
{
    public sealed class InputChangeListTests : XunitBuildXLTest
    {
        public InputChangeListTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestValidInputChangeList()
        {
            Verify(new[]
            {
                (A("C", new[]{ "D", "E", "f"}), PathChanges.DataOrMetadataChanged.ToString()),
                (A("C", new[]{ "D", "X", "g"}), (PathChanges.DataOrMetadataChanged | PathChanges.Removed).ToString()),
                (A("C", new[]{ "D", "X", "h"}), null),
                (A("C", new[]{ "D", "X", "i"}), PathChanges.NewlyPresent.ToString())
            },
            shouldSucceed: true);
        }

        [Fact]
        public void TestInvalidPathInInputChangeList()
        {
            Verify(new[]
            {
                (A("C", new[]{ "D", "E", "f"}), PathChanges.DataOrMetadataChanged.ToString()),
                (R("X", "Y", "m"), (PathChanges.DataOrMetadataChanged | PathChanges.Removed).ToString()),
                (A("C", new[]{ "D", "X", "i"}), PathChanges.NewlyPresent.ToString())
            },
            shouldSucceed: false);
        }

        [Fact]
        public void TestInvalidChangesInChangeList()
        {
            Verify(new[]
            {
                (A("C", new[]{ "D", "E", "f"}), PathChanges.DataOrMetadataChanged.ToString()),
                (A("C", new[]{ "D", "X", "g"}), "Foo"),
                (A("C", new[]{ "D", "X", "i"}), PathChanges.NewlyPresent.ToString())
            },
            shouldSucceed: false);
        }

        [Fact]
        public void TestRelativePathsWithoutSourceRoot()
        {
            Verify(new[]
            {
                (A("C", new[]{ "D", "E", "f"}), PathChanges.DataOrMetadataChanged.ToString()),
                (R(new[]{ "D", "X", "i"}), PathChanges.NewlyPresent.ToString())
            },
            shouldSucceed: false);
        }

        [Fact]
        public void TestRelativePathsWithSourceRoot()
        {
            Verify(new[]
            {
                (A("C", new[]{ "D", "E", "f"}), PathChanges.DataOrMetadataChanged.ToString()),
                (R(new[]{ "D", "..", "i"}), PathChanges.NewlyPresent.ToString()),
                (R(new[]{ "M", "N", "o"}), PathChanges.NewlyPresent.ToString())
            },
            shouldSucceed: true,
            sourceRoot: A("C", new[] {"X", "Y", "Z" }));
        }

        [Fact]
        public void TestWithDirectoryTranslator()
        {
            var directoryTranslator = new DirectoryTranslator();
            directoryTranslator.AddTranslation(A("C", new[] { "D", "E" }), A("C", new[] { "X", "Y" }));
            directoryTranslator.Seal();

            Verify(new[]
            {
                (A("C", new[]{ "D", "E", "f"}), PathChanges.DataOrMetadataChanged.ToString()),
            },
            shouldSucceed: true,
            directoryTranslator: directoryTranslator);
        }

        [Fact]
        public void TestFreeString()
        {
            XAssert.IsNotNull(Test(new[] { "" }));
            XAssert.IsNull(Test(new[] { "|" }));
            XAssert.IsNull(Test(new[] { "?abc|foo" }));
            XAssert.IsNull(Test(new[] { "?abc|fo|o" }));
        }

        private InputChangeList Test(string[] changedPaths, string sourceRoot = null, DirectoryTranslator directoryTranslator = null)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (StreamWriter writer = new StreamWriter(ms, System.Text.Encoding.UTF8, 4096, true))
                {
                    foreach (var changedPath in changedPaths)
                    {
                        writer.WriteLine(changedPath);
                    }
                }

                ms.Seek(0, SeekOrigin.Begin);

                using (StreamReader reader = new StreamReader(ms, System.Text.Encoding.UTF8, false, 4096, true))
                {
                    return InputChangeList.CreateFromStream(new LoggingContext("Dummy"), reader, null, sourceRoot, directoryTranslator);
                }
            }
        }

        private void Verify((string path, string changes)[] changedPathsToWrite, bool shouldSucceed, string sourceRoot = null, DirectoryTranslator directoryTranslator = null)
        {
            var inputChangeList = Test(changedPathsToWrite.Select(cp => CreateInputLine(cp.path, cp.changes)).ToArray(), sourceRoot, directoryTranslator);
            XAssert.AreEqual(shouldSucceed, inputChangeList != null);

            if (shouldSucceed)
            {
                var changedPathsRead = inputChangeList.ChangedPaths.ToArray();
                XAssert.AreEqual(changedPathsToWrite.Length, changedPathsRead.Length);

                for (int i = 0; i < changedPathsRead.Length; ++i)
                {
                    string expectedPath = changedPathsToWrite[i].path;

                    if (!Path.IsPathRooted(expectedPath))
                    {
                        XAssert.IsTrue(!string.IsNullOrEmpty(sourceRoot));
                        expectedPath = Path.GetFullPath(Path.Combine(sourceRoot, expectedPath));
                    }

                    if (directoryTranslator != null)
                    {
                        expectedPath = directoryTranslator.Translate(expectedPath);
                    }

                    XAssert.AreEqual(expectedPath, changedPathsRead[i].Path);
                    XAssert.AreEqual(
                        !string.IsNullOrEmpty(changedPathsToWrite[i].changes)
                        ? (PathChanges)Enum.Parse(typeof(PathChanges), changedPathsToWrite[i].changes)
                        : PathChanges.DataOrMetadataChanged, 
                        changedPathsRead[i].PathChanges);
                }
            }

            string CreateInputLine(string p, string c)
            {
                c = !string.IsNullOrEmpty(c) ? "|" + c : string.Empty;
                return p + c;
            }
        }
    }
}
