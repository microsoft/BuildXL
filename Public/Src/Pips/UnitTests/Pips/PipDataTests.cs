// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Pips
{
    public sealed class PipDataTests : XunitBuildXLTest
    {
        private readonly PathTable m_pathTable;
        private readonly AbsolutePath m_uniqueEntry0;
        private StringId m_expectedStringId0;
        private const string ExpectedString0 = "expectedString0";
        private const string CaseVaryingString = "CASE_varying_String";
        private readonly string m_separator0 = string.Empty;
        private const string Separator1 = "\n";
        private const PipDataFragmentEscaping Escaping0 = PipDataFragmentEscaping.CRuntimeArgumentRules;
        private const PipDataFragmentEscaping Escaping1 = PipDataFragmentEscaping.NoEscaping;
        private const string UniqueEntry1 = "unique to fragment 0";
        private readonly PipDataBuilder m_pipDataBuilder;
        private const PipDataFragmentEscaping EnclosingEscaping = PipDataFragmentEscaping.CRuntimeArgumentRules;
        private const string EnclosingSeparator = " #S ";
        private readonly PipDataBuilder.Cursor m_cursorStart;
        private readonly PipDataBuilder.Cursor m_cursor0;
        private readonly PipDataBuilder.Cursor m_cursor1;
        private readonly PipDataBuilder.Cursor m_cursorEnd;

        public PipDataTests(ITestOutputHelper output)
            : base(output)
        {
            m_pathTable = new PathTable();
            m_expectedStringId0 = StringId.Create(m_pathTable.StringTable, ExpectedString0);
            m_pipDataBuilder = new PipDataBuilder(m_pathTable.StringTable);

            m_expectedStringId0 = StringId.Create(m_pathTable.StringTable, ExpectedString0);
            m_uniqueEntry0 = AbsolutePath.Create(m_pathTable, A("c", "unique to fragment 0"));

            // BEGIN ADDING ARGUMENTS
            m_cursorStart = m_pipDataBuilder.CreateCursor();

            AddStandardBlock(m_pipDataBuilder);

            m_cursor0 = m_pipDataBuilder.CreateCursor();

            using (m_pipDataBuilder.StartFragment(Escaping0, m_separator0))
            {
                m_pipDataBuilder.Add(m_uniqueEntry0);
                AddStandardBlock(m_pipDataBuilder);
            }

            m_cursor1 = m_pipDataBuilder.CreateCursor();

            using (m_pipDataBuilder.StartFragment(Escaping1, Separator1))
            {
                AddStandardBlock(m_pipDataBuilder);
                m_pipDataBuilder.Add(UniqueEntry1);
            }

            m_cursorEnd = m_pipDataBuilder.CreateCursor();

            // END ADDING ARGUMENTS
        }

        [Fact]
        public void PipDataReadWrite()
        {
            var pipData = m_pipDataBuilder.ToPipData(EnclosingSeparator, EnclosingEscaping);

            byte[] bytes = new byte[10000];
            int startIndex = 23;
            int index = startIndex;

            foreach (var entry in pipData.Entries)
            {
                var expectedIndex = index + PipDataEntry.BinarySize;
                entry.Write(bytes, ref index);

                // Ensure the correct number of bytes are written
                Assert.Equal(expectedIndex, index);
            }

            var endIndex = index;
            index = startIndex;

            List<PipDataEntry> readEntries = new List<PipDataEntry>();

            while (index < endIndex)
            {
                var expectedIndex = index + PipDataEntry.BinarySize;
                readEntries.Add(PipDataEntry.Read(bytes, ref index));

                // Ensure the correct number of bytes are read
                Assert.Equal(expectedIndex, index);
            }

            var readPipData = PipData.CreateInternal(pipData.HeaderEntry, PipDataEntryList.FromEntries(readEntries), StringId.Invalid);
            VerifyFullPipData(readPipData);
        }

        [Fact]
        public void BasicPipData()
        {
            var pipData = m_pipDataBuilder.ToPipData(EnclosingSeparator, EnclosingEscaping);
            VerifyFullPipData(pipData);
        }

        [Fact]
        public void CopiedPipDataFragment()
        {
            var pipData = m_pipDataBuilder.ToPipData(EnclosingSeparator, EnclosingEscaping);

            // Create a pip fragment and verify that fragment
            var pipFragment = PipFragment.CreateNestedFragment(pipData);

            XAssert.IsTrue(pipFragment.FragmentType == PipFragmentType.NestedFragment);
            VerifyFullPipData(pipFragment.GetNestedFragmentValue());
        }

        [Fact]
        public void CreatePipDataWithCursors()
        {
            var pipDataStart = m_pipDataBuilder.ToPipData(EnclosingSeparator, EnclosingEscaping, m_cursorStart);
            var pipData0 = m_pipDataBuilder.ToPipData(EnclosingSeparator, EnclosingEscaping, m_cursor0);
            var pipData1 = m_pipDataBuilder.ToPipData(EnclosingSeparator, EnclosingEscaping, m_cursor1);
            var pipDataEnd = m_pipDataBuilder.ToPipData(EnclosingSeparator, EnclosingEscaping, m_cursorEnd);

            VerifyFullPipData(pipDataStart);

            XAssert.AreEqual(2, pipData0.FragmentCount);
            VerifyPipDataFromCursor0(pipData0.GetEnumerator());

            XAssert.AreEqual(1, pipData1.FragmentCount);
            VerifyPipDataFromCursor1(pipData1.GetEnumerator());

            XAssert.AreEqual(0, pipDataEnd.FragmentCount);
            VerifyPipDataFromCursorEnd(pipDataEnd.GetEnumerator());
        }

        private void VerifyFullPipData(PipData pipData)
        {
            var enumerator = pipData.GetEnumerator();
            XAssert.AreEqual(NumStandardBlockFragments + 2, pipData.FragmentCount);

            VerifyStandardBlock(ref enumerator);

            VerifyPipDataFromCursor0(enumerator);
        }

        private void VerifyPipDataFromCursor0(PipData.FragmentEnumerator enumerator)
        {
            PipFragment fragment;

            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.NestedFragment);
            var nestedFragment0 = fragment;
            VerifyNested0(nestedFragment0);

            VerifyPipDataFromCursor1(enumerator);
        }

        private void VerifyPipDataFromCursor1(PipData.FragmentEnumerator enumerator)
        {
            PipFragment fragment;

            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.NestedFragment);
            var nestedFragment1 = fragment;
            VerifyNested1(nestedFragment1);

            VerifyPipDataFromCursorEnd(enumerator);
        }

        private void VerifyPipDataFromCursorEnd(PipData.FragmentEnumerator enumerator)
        {
            XAssert.IsFalse(enumerator.MoveNext());
        }

        private void VerifyNested0(PipFragment fragment)
        {
            PipData nestedData = fragment.GetNestedFragmentValue();
            XAssert.AreEqual(NumStandardBlockFragments + 1, nestedData.FragmentCount);
            using (var nestedEnumerator = nestedData.GetEnumerator())
            {
                var localEnumerator = nestedEnumerator;

                AssertMoveNext(ref localEnumerator, out fragment, PipFragmentType.AbsolutePath);
                XAssert.AreEqual(m_uniqueEntry0, fragment.GetPathValue());

                VerifyStandardBlock(ref localEnumerator);

                XAssert.IsFalse(localEnumerator.MoveNext());
            }
        }

        private void VerifyNested1(PipFragment fragment)
        {
            PipData nestedData = fragment.GetNestedFragmentValue();
            XAssert.AreEqual(NumStandardBlockFragments + 1, nestedData.FragmentCount);
            using (var nestedEnumerator = nestedData.GetEnumerator())
            {
                var localEnumerator = nestedEnumerator;

                VerifyStandardBlock(ref localEnumerator);

                AssertMoveNext(ref localEnumerator, out fragment, PipFragmentType.StringLiteral);
                XAssert.AreEqual(UniqueEntry1, m_pathTable.StringTable.GetString(fragment.GetStringIdValue()));

                XAssert.IsFalse(localEnumerator.MoveNext());
            }
        }

        private AbsolutePath Path1 => AbsolutePath.Create(m_pathTable, Path.Combine(A(@"x", "absolute", "dir"), CaseVaryingString.ToUpper()));

        private AbsolutePath Path2 => AbsolutePath.Create(m_pathTable, Path.Combine(A("x", "absolutePath"), CaseVaryingString.ToUpper()));

        private FileArtifact SourceFile => FileArtifact.CreateSourceFile(Path1);

        private FileArtifact OutputFile => FileArtifact.CreateOutputFile(Path1);

        private FileArtifact RewrittenFile => OutputFile.CreateNextWrittenVersion();

        private FileArtifact RewrittenFile2 => RewrittenFile.CreateNextWrittenVersion();

        private const int NumStandardBlockFragments = 8; // this number must match the number of fragments added in AddStandardBlock

        private void AddStandardBlock(PipDataBuilder pipDataBuilder)
        {
            pipDataBuilder.Add(Path1);
            pipDataBuilder.Add(Path2);
            pipDataBuilder.Add(CaseVaryingString);
            pipDataBuilder.Add(m_expectedStringId0);
            pipDataBuilder.AddVsoHash(SourceFile);
            pipDataBuilder.AddVsoHash(OutputFile);
            pipDataBuilder.AddVsoHash(RewrittenFile);
            pipDataBuilder.AddVsoHash(RewrittenFile2);

            // if you add more fragments here, update
            //   (1) NumStandardBlockFragments constant above
            //   (2) VerifyStandardBlock method below
        }

        private void VerifyStandardBlock(ref PipData.FragmentEnumerator enumerator)
        {
            PipFragment fragment;
            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.AbsolutePath);
            XAssert.AreEqual(Path1, fragment.GetPathValue());

            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.AbsolutePath);
            XAssert.AreEqual(Path2, fragment.GetPathValue());

            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.StringLiteral);
            XAssert.AreEqual(CaseVaryingString, m_pathTable.StringTable.GetString(fragment.GetStringIdValue()));

            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.StringLiteral);
            XAssert.AreEqual(m_expectedStringId0, fragment.GetStringIdValue());

            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.VsoHash);
            XAssert.AreEqual(SourceFile, fragment.GetFileValue());

            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.VsoHash);
            XAssert.AreEqual(OutputFile, fragment.GetFileValue());

            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.VsoHash);
            XAssert.AreEqual(RewrittenFile, fragment.GetFileValue());

            AssertMoveNext(ref enumerator, out fragment, PipFragmentType.VsoHash);
            XAssert.AreEqual(RewrittenFile2, fragment.GetFileValue());
        }

        private static void AssertMoveNext(ref PipData.FragmentEnumerator enumerator, out PipFragment fragment, PipFragmentType? expectedFragmentType = null)
        {
            XAssert.IsTrue(enumerator.MoveNext());
            fragment = enumerator.Current;
            XAssert.IsTrue(fragment.IsValid);
            if (expectedFragmentType.HasValue)
            {
                XAssert.AreEqual(expectedFragmentType.Value, fragment.FragmentType);
            }
        }
    }
}
