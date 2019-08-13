﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Storage.InputChange;
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

        private InputChangeList Test((string path, string changes)[] changedPaths)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (StreamWriter writer = new StreamWriter(ms, System.Text.Encoding.UTF8, 4096, true))
                {
                    foreach (var changedPath in changedPaths)
                    {
                        var changes = !string.IsNullOrEmpty(changedPath.changes) ? "|" + changedPath.changes : string.Empty;
                        writer.WriteLine(changedPath.path + changes);
                    }
                }

                ms.Seek(0, SeekOrigin.Begin);

                using (StreamReader reader = new StreamReader(ms, System.Text.Encoding.UTF8, false, 4096, true))
                {
                    return InputChangeList.CreateFromStream(new LoggingContext("Dummy"), reader);
                }
            }
        }

        private void Verify((string path, string changes)[] changedPathsToWrite, bool shouldSucceed)
        {
            var inputChangeList = Test(changedPathsToWrite.Select(cp => (cp.path, cp.changes)).ToArray());
            XAssert.AreEqual(shouldSucceed, inputChangeList != null);

            if (shouldSucceed)
            {
                var changedPathsRead = inputChangeList.ChangedPaths.ToArray();
                XAssert.AreEqual(changedPathsToWrite.Length, changedPathsRead.Length);

                for (int i = 0; i < changedPathsRead.Length; ++i)
                {
                    XAssert.AreEqual(changedPathsToWrite[i].path, changedPathsRead[i].Path);
                    XAssert.AreEqual(
                        !string.IsNullOrEmpty(changedPathsToWrite[i].changes)
                        ? (PathChanges)Enum.Parse(typeof(PathChanges), changedPathsToWrite[i].changes)
                        : PathChanges.DataOrMetadataChanged, 
                        changedPathsRead[i].PathChanges);
                }
            }
        }
    }
}
