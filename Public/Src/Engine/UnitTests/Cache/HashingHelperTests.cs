// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text;
using BuildXL.Engine.Cache;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine.Cache
{
    public sealed class HashingHelperTests : TemporaryStorageTestBase
    {
        public HashingHelperTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static readonly string FakeUserProfilePath = X("/X/Users/FAKEUSERNAME");

        private static readonly string FakeInternetCachePath = R(FakeUserProfilePath, "TempInternet");

        private static readonly string FakeInternetHistoryPath = R(FakeUserProfilePath, "InternetHistory");

        private static readonly string FancyPath = X("/x/fancyPath/lol");

        [Fact]
        public void Int()
        {
            VerifyFingerprintText(
                (h, pt) => h.Add("Abc", 123),
                "[[00000003]Abc:Int]0000007B");
        }

        [Fact]
        public void Long()
        {
            VerifyFingerprintText(
                (h, pt) => h.Add("Abc", 123L + (123L << 32)),
                "[[00000003]Abc:Long]0000007B0000007B");
        }

        [Fact]
        public void String()
        {
            VerifyFingerprintText(
                (h, pt) => h.Add("Abc", "DEfg"),
                "[[00000003]Abc:String][00000004]DEfg");
        }

        [Fact]
        public void VerifyContentHash()
        {
            VerifyFingerprintText(
                (h, pt) => h.Add("Abc", ContentHashingUtilities.EmptyHash),
                "[[00000003]Abc:ContentHash]1E57CF2792A900D06C1CDFB3C453F35BC86F72788AA9724C96C929D1CC6B456A00");
        }

        [Fact]
        public void Path()
        {
            VerifyFingerprintText(
                (h, pt) =>
                {
                    AbsolutePath path = AbsolutePath.Create(pt, FancyPath);
                    h.Add(path);
                },
                $"[Path][00000010]{FancyPath.ToUpperInvariant()}");
        }

        [Fact]
        public void UserProfilePath()
        {
            string absPath = R(FakeUserProfilePath, "lol");
            VerifyFingerprintText(
                (h, pt) =>
                {
                    h.Add(AbsolutePath.Create(pt, absPath));
                },
                $"[Path][00000019]{absPath.ToUpperInvariant()}");
        }

        [Fact]
        public void InternetCachePath()
        {
            string absPath = R(FakeInternetCachePath, "lol");
            VerifyFingerprintText(
                (h, pt) =>
                {
                    h.Add(AbsolutePath.Create(pt, absPath));
                },
                $"[Path][00000026]{absPath.ToUpperInvariant()}");
        }

        [Fact]
        public void InternetHistoryPath()
        {
            string absPath = R(FakeInternetHistoryPath, "lol");
            VerifyFingerprintText(
                (h, pt) =>
                {
                    h.Add(AbsolutePath.Create(pt, absPath));
                },
                $"[Path][00000029]{absPath.ToUpperInvariant()}");
        }

        [Fact]
        public void NamedPath()
        {
            VerifyFingerprintText(
                (h, pt) =>
                {
                    h.Add("Abc", AbsolutePath.Create(pt, FancyPath));
                },
                $"[[00000003]Abc:Path][00000010]{FancyPath.ToUpperInvariant()}");
        }

        [Fact]
        public void PathAndContentHash()
        {
            VerifyFingerprintText(
                (h, pt) =>
                {
                    AbsolutePath path = AbsolutePath.Create(pt, FancyPath);
                    h.Add(path, ContentHashingUtilities.EmptyHash);
                },
                $"[HashedPath][00000010]{FancyPath.ToUpperInvariant()}|1E57CF2792A900D06C1CDFB3C453F35BC86F72788AA9724C96C929D1CC6B456A00");
        }

        [Fact]
        public void NamedPathAndContentHash()
        {
            VerifyFingerprintText(
                (h, pt) =>
                {
                    AbsolutePath path = AbsolutePath.Create(pt, FancyPath);
                    h.Add("Abc", path, ContentHashingUtilities.EmptyHash);
                },
                $"[[00000003]Abc:HashedPath][00000010]{FancyPath.ToUpperInvariant()}|1E57CF2792A900D06C1CDFB3C453F35BC86F72788AA9724C96C929D1CC6B456A00");
        }

        [Fact]
        public void Indent()
        {
            VerifyFingerprintText(
                (h, pt) =>
                {
                    h.Indent();
                    h.Indent();
                    h.Add("Abc", 123);
                    h.Unindent();
                    h.Add("Def", 456);
                },
                "  [[00000003]Abc:Int]0000007B",
                " [[00000003]Def:Int]000001C8");
        }

        [Fact]
        public void Collection()
        {
            var magicNumbers = new long[3];
            for (int i = 0; i < magicNumbers.Length; i++)
            {
                magicNumbers[i] = i;
            }

            VerifyFingerprintText(
                (h, pt) => { h.AddCollection<long, long[]>("Numbers", magicNumbers, (hh, n) => hh.Add("Val", n)); },
                " [[00000003]Val:Long]0000000000000000",
                " [[00000003]Val:Long]0000000000000001",
                " [[00000003]Val:Long]0000000000000002",
                "[[00000007]Numbers:Int]00000003");
        }

        [Fact]
        public void BufferReallocation()
        {
            const int TargetSize = 8192;
            const string Item = "[[00000003]Abc:Int]0000007B";
            int itemLengthInBytes = Encoding.Unicode.GetByteCount(Item);
            int numIterations = (TargetSize / itemLengthInBytes) + 1;

            var expectedLines = new string[numIterations];
            for (int i = 0; i < numIterations; i++)
            {
                expectedLines[i] = Item;
            }

            VerifyFingerprintText(
                (h, pt) =>
                {
                    for (int i = 0; i < numIterations; i++)
                    {
                        h.Add("Abc", 123);
                    }
                },
                expectedLines);
        }

        private void VerifyFingerprintText(Action<HashingHelper, PathTable> addStream, params string[] expectedTextLines)
        {
            VerifyFingerprintText(addStream, string.Join("\r\n", expectedTextLines));
        }

        private void VerifyFingerprintText(Action<HashingHelper, PathTable> addStream, string expectedText)
        {
            // Need a trailing newline for even a single item.
            expectedText = expectedText + "\r\n";

            var pathTable = new PathTable();
            using (var withText = new HashingHelper(pathTable, recordFingerprintString: true))
            {
                addStream(withText, pathTable);

                Fingerprint actualHash = FingerprintUtilities.CreateFrom(withText.GenerateHash().ToByteArray());
                string actualText = withText.FingerprintInputText;

                XAssert.AreEqual(expectedText, actualText, "Fingerprint text mismatched.");
            }
        }
    }
}
