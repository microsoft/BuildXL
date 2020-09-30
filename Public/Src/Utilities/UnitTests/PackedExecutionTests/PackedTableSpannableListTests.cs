// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedTable;
using System;
using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    public class PackedTableSpannableListTests : TemporaryStorageTestBase
    {
        public PackedTableSpannableListTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void SpannableList_can_be_constructed()
        {
            SpannableList<int> list = new SpannableList<int>();

            XAssert.AreEqual(0, list.Count);
            XAssert.AreEqual(0, list.Count());

            XAssert.IsFalse(list.Contains(0));
            XAssert.AreEqual(-1, list.IndexOf(0));
            XAssert.AreEqual(0, list.AsSpan().Length);
        }

        [Fact]
        public void SpannableList_can_be_appended()
        {
            SpannableList<int> list = new SpannableList<int>(1);

            XAssert.AreEqual(0, list.Count);
            XAssert.AreEqual(0, list.Count());

            XAssert.IsFalse(list.Contains(1));
            XAssert.AreEqual(-1, list.IndexOf(1));
            XAssert.AreEqual(0, list.AsSpan().Length);

            list.Add(1);
            XAssert.AreEqual(1, list.Count);
            XAssert.AreEqual(1, list.Count());
            XAssert.IsTrue(list.Contains(1));
            XAssert.AreEqual(0, list.IndexOf(1));
            XAssert.AreEqual(1, list.AsSpan().Length);
            XAssert.AreEqual(1, list.AsSpan()[0]);

            list.Add(2);
            XAssert.AreEqual(2, list.Count);
            XAssert.AreEqual(2, list.Count());
            XAssert.IsTrue(list.Contains(1));
            XAssert.IsTrue(list.Contains(2));
            XAssert.IsFalse(list.Contains(3));
            XAssert.AreEqual(0, list.IndexOf(1));
            XAssert.AreEqual(1, list.IndexOf(2));
            XAssert.AreEqual(2, list.AsSpan().Length);
            XAssert.AreEqual(1, list.AsSpan()[0]);
            XAssert.AreEqual(2, list.AsSpan()[1]);

            list.AddRange(new[] { 3, 4, 5 }.AsSpan());
            XAssert.AreArraysEqual(new[] { 1, 2, 3, 4, 5 }, list.ToArray(), true);

            XAssert.AreArraysEqual(new[] { 3, 4 }, list.Enumerate(2, 2).ToArray(), true);

            XAssert.AreArraysEqual(new[] { 3, 4 }, list.AsSpan().Slice(2, 2).ToArray(), true);
        }

        [Fact]
        public void SpannableList_can_be_inserted()
        {
            SpannableList<int> list = new SpannableList<int>(1);

            list.Insert(0, 1);
            XAssert.AreEqual(1, list.Count);
            XAssert.AreEqual(1, list.Count());
            XAssert.IsTrue(list.Contains(1));
            XAssert.AreEqual(0, list.IndexOf(1));
            XAssert.AreEqual(1, list.AsSpan().Length);
            XAssert.AreEqual("SpannableList<Int32>[1]{ 1 }", list.ToFullString());

            list.Insert(0, 2);
            XAssert.AreEqual(2, list.Count);
            XAssert.AreEqual(2, list.Count());
            XAssert.IsTrue(list.Contains(1));
            XAssert.IsTrue(list.Contains(2));
            XAssert.IsFalse(list.Contains(3));
            XAssert.AreEqual(1, list.IndexOf(1));
            XAssert.AreEqual(0, list.IndexOf(2));
            XAssert.AreEqual(2, list.AsSpan().Length);
            XAssert.AreEqual(2, list.AsSpan()[0]);
            XAssert.AreEqual(1, list.AsSpan()[1]);

            list.Insert(2, 3);
            XAssert.AreEqual(3, list.Count);
            XAssert.AreEqual(3, list.Count());
            XAssert.IsTrue(list.Contains(1));
            XAssert.IsTrue(list.Contains(2));
            XAssert.IsTrue(list.Contains(3));
            XAssert.AreEqual(1, list.IndexOf(1));
            XAssert.AreEqual(0, list.IndexOf(2));
            XAssert.AreEqual(2, list.IndexOf(3));
            XAssert.AreEqual(3, list.AsSpan().Length);
            XAssert.AreArraysEqual(new[] { 2, 1, 3 }, list.ToArray(), true);
        }

        [Fact]
        public void SpannableList_can_be_removed()
        {
            SpannableList<int> list = new SpannableList<int>(1);

            list.Add(1);
            list.Add(2);
            list.Add(3);

            XAssert.AreArraysEqual(new[] { 1, 2, 3 }, list.ToArray(), true);

            list.RemoveAt(0);

            XAssert.AreEqual(2, list.Count);
            XAssert.AreArraysEqual(new[] { 2, 3 }, list.ToArray(), true);

            list.RemoveAt(1);

            XAssert.AreEqual(1, list.Count);
            XAssert.AreArraysEqual(new[] { 2 }, list.ToArray(), true);

            XAssert.IsFalse(list.Remove(1));
            XAssert.IsTrue(list.Remove(2));
            XAssert.AreEqual(0, list.Count);
        }
    }
}
