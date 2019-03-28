// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System.IO;

namespace Test.BuildXL.Utilities
{
    public sealed class HierarchicalNameTableTests : XunitBuildXLTest
    {
        public HierarchicalNameTableTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Basic()
        {
            var hnt = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            int c1 = hnt.Count;
            XAssert.IsTrue(c1 > 0);

            HierarchicalNameId id = hnt.AddName(A("c","a","b","c"));
            string str = hnt.ExpandName(id);
            XAssert.AreEqual(A("c", "a", "b", "c"), str);

            int c2 = hnt.Count;
            XAssert.IsTrue(c2 > c1);

            hnt.AddName(A("c", "a", "b", "c", "d"));
            hnt.AddName(A("c", "a", "b", "c"));

            int c3 = hnt.Count;
            XAssert.IsTrue(c3 > c2);

            int size = hnt.SizeInBytes;
            XAssert.IsTrue(size > 0);

            if (OperatingSystemHelper.IsUnixOS)
            {
                var id2 = hnt.AddName("/");
                c3 = hnt.Count;
                XAssert.AreEqual("/", hnt.ExpandName(id2));
                XAssert.ArrayEqual(new[] { id2 }, hnt.EnumerateHierarchyBottomUp(id2).ToArray());
            }

            hnt.Freeze();

            size = hnt.SizeInBytes;
            XAssert.IsTrue(size > 0);

            int c4 = hnt.Count;
            XAssert.AreEqual(c3, c4);
        }

        [Fact]
        public void IsWithin()
        {
            var hnt = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId tree = hnt.AddName(A("c"));
            HierarchicalNameId path = hnt.AddName(A("c","windows"));
            XAssert.IsTrue(hnt.IsWithin(tree, path));
            XAssert.IsFalse(hnt.IsWithin(path, tree));
            XAssert.IsTrue(hnt.IsWithin(tree, tree));
            XAssert.IsTrue(hnt.IsWithin(path, path));
        }

        [Fact]
        public void ExpandFinalComponent()
        {
            var hnt = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId id = hnt.AddName(A("c","a","b","c"));
            XAssert.AreEqual("c", hnt.ExpandFinalComponent(id));

            id = hnt.GetContainer(id);
            XAssert.AreEqual("b", hnt.ExpandFinalComponent(id));

            id = hnt.GetContainer(id);
            XAssert.AreEqual("a", hnt.ExpandFinalComponent(id));

            id = hnt.GetContainer(id);
            XAssert.AreEqual(OperatingSystemHelper.IsUnixOS ? Path.VolumeSeparatorChar.ToString() : "c:", hnt.ExpandFinalComponent(id));
        }

        [Fact]
        public void ManyNames()
        {
            var hnt = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            var sb = new StringBuilder();

            for (char ch1 = '\u0000'; ch1 <= '\ufffe'; ch1++)
            {
                char ch2 = 'a';
                sb.Length = 0;
                sb.AppendFormat("C:\\{0}\\{1}", ch1, ch2);
                hnt.AddName(sb.ToString());
            }
        }

        [Fact]
        public void TryExpandNameRelativeToAnother()
        {
            var hnt = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId root = hnt.AddName(A("c","dir","root"));
            HierarchicalNameId immediateDescendant = hnt.AddName(A("c", "dir","root","file"));
            HierarchicalNameId furtherDescendant = hnt.AddName(A("c","dir","root","moredir","file2"));
            HierarchicalNameId sibling = hnt.AddName(A("c","dir","sibling"));

            string immediateDescendantPath;
            XAssert.IsTrue(hnt.TryExpandNameRelativeToAnother(root, immediateDescendant, out immediateDescendantPath));
            XAssert.AreEqual("file", immediateDescendantPath);

            string furtherDescendantPath;
            XAssert.IsTrue(hnt.TryExpandNameRelativeToAnother(root, furtherDescendant, out furtherDescendantPath));
            XAssert.AreEqual(R("moredir","file2"), furtherDescendantPath);

            string siblingPath;
            XAssert.IsFalse(hnt.TryExpandNameRelativeToAnother(root, sibling, out siblingPath));
            XAssert.IsNull(siblingPath);

            string emptyPath;
            XAssert.IsTrue(hnt.TryExpandNameRelativeToAnother(root, root, out emptyPath));
            XAssert.AreEqual(string.Empty, emptyPath);
        }

        [Fact]
        public void GetContainer()
        {
            var hnt = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId cid = hnt.AddName(A("c","a","b","c"));
            HierarchicalNameId bid = hnt.GetContainer(cid);
            HierarchicalNameId aid = hnt.GetContainer(bid);
            HierarchicalNameId root = hnt.GetContainer(aid);
            HierarchicalNameId invalid = hnt.GetContainer(root);

            XAssert.AreEqual(A("c", "a", "b", "c"), hnt.ExpandName(cid));
            XAssert.AreEqual(A("c", "a", "b"), hnt.ExpandName(bid));
            XAssert.AreEqual(A("c", "a"), hnt.ExpandName(aid));
            XAssert.AreEqual(OperatingSystemHelper.IsUnixOS ? Path.VolumeSeparatorChar.ToString() : "c:", hnt.ExpandName(root));
            XAssert.AreEqual(HierarchicalNameId.Invalid, invalid);
        }

        [Fact]
        public void TryGetValue()
        {
            var hnt = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId other;

            XAssert.IsFalse(hnt.TryGetName(string.Empty, out other));

            XAssert.IsFalse(hnt.TryGetName(A("c","d"), out other));

            HierarchicalNameId id = hnt.AddName(A("c","a","b","c"));
            XAssert.IsTrue(hnt.TryGetName(A("c", "a", "b", "c"), out other));
            XAssert.AreEqual(id, other);

            XAssert.IsTrue(hnt.TryGetName(A("c", "a", "b"), out other));

            XAssert.IsFalse(hnt.TryGetName(A("c","d"), out other));
        }

        [Fact]
        public void IsValid()
        {
            HierarchicalNameId path = HierarchicalNameId.Invalid;
            XAssert.IsFalse(path.IsValid);
        }

        [Fact]
        public void HierarchicalNameIdEquality()
        {
            StructTester.TestEquality(
                baseValue: new HierarchicalNameId(123),
                equalValue: new HierarchicalNameId(123),
                notEqualValues: new HierarchicalNameId[] { HierarchicalNameId.Invalid, new HierarchicalNameId(124) },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b);
        }

        [Fact]
        public void CaseFolding()
        {
            var hnt = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);

            // shouldn't be interference between different hierarchies and case should be preserved
            HierarchicalNameId id1 = hnt.AddName(A("c","a","b","c"));
            HierarchicalNameId id3 = hnt.AddName(A("c","A","B","C"));

            HierarchicalNameId id2 = hnt.AddName(A("c","X","A","B","C"));
            XAssert.AreEqual(A("c","a","b","c"), hnt.ExpandName(id1));
            XAssert.AreEqual(A("c","X","A","B","C"), hnt.ExpandName(id2));

            // we expect to find an existing path when using different casing
            // HierarchicalNameId id3 = hnt.AddName((A("c","\A\B\C");
            XAssert.AreEqual(id1, id3);

            // and we expect for common paths to have "first one in wins" casing
            HierarchicalNameId id4 = hnt.AddName(A("c","A","B","C","D"));
            XAssert.AreEqual(A("c","a","b","c","D"), hnt.ExpandName(id4));
        }

        [Fact]
        public void IgnoreCase()
        {
            var hnt = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            {
                HierarchicalNameId id1 = hnt.AddName("A"+Path.DirectorySeparatorChar+"B");
                HierarchicalNameId id2 = hnt.AddName("a" + Path.DirectorySeparatorChar + "b");
                XAssert.AreEqual(id1, id2);
            }

            hnt = new HierarchicalNameTable(new StringTable(), false, Path.DirectorySeparatorChar);
            {
                HierarchicalNameId id1 = hnt.AddName("A" + Path.DirectorySeparatorChar + "B");
                HierarchicalNameId id2 = hnt.AddName("a" + Path.DirectorySeparatorChar + "b");
                XAssert.AreNotEqual(id1, id2);
            }
        }

        [Fact]
        public void EnumeratingBottomUp()
        {
            var ht = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            ht.AddName(R("a", "b", "sibling", "sibling.txt"));
            HierarchicalNameId name = ht.AddName(R("a", "b", "c", "d.cpp"));

            var enumeration = ht.EnumerateHierarchyBottomUp(name).ToArray();
            var expectation = new[]
            {
                ht.AddName(R("a","b","c","d.cpp")),
                ht.AddName(R("a","b","c")),
                ht.AddName(R("a","b")),
                ht.AddName(R("a"))
            };

            Assert.Equal(enumeration, expectation);
        }

        [Fact]
        public void GetAndSetFlags()
        {
            var ht = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId tag1 = ht.AddName(R("c", "tag1"));
            HierarchicalNameId tag2 = ht.AddName(R("c", "tag2"));

            XAssert.AreEqual(ht.GetContainer(tag1), ht.GetContainerAndFlags(tag1).Item1);

            XAssert.IsTrue(ht.SetFlags(tag2, HierarchicalNameTable.NameFlags.Marked));

            XAssert.IsTrue(ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Marked));
            XAssert.IsFalse(ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Marked));
            XAssert.AreEqual(HierarchicalNameTable.NameFlags.Marked, ht.GetContainerAndFlags(tag1).Item2);

            XAssert.IsTrue(ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Sealed));
            XAssert.AreEqual(HierarchicalNameTable.NameFlags.Marked | HierarchicalNameTable.NameFlags.Sealed, ht.GetContainerAndFlags(tag1).Item2);

            XAssert.IsTrue(ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Sealed, clear: true));
            XAssert.AreEqual(HierarchicalNameTable.NameFlags.Marked, ht.GetContainerAndFlags(tag1).Item2);

            XAssert.IsFalse(ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Marked | HierarchicalNameTable.NameFlags.Sealed, clear: true));
            XAssert.AreEqual(HierarchicalNameTable.NameFlags.None, ht.GetContainerAndFlags(tag1).Item2);

            // tag2 should be the same as before
            XAssert.AreEqual(HierarchicalNameTable.NameFlags.Marked, ht.GetContainerAndFlags(tag2).Item2);
        }

        [Fact]
        public Task ConcurrentGetAndSetFlags()
        {
            var tasks = new List<Task>();

            // This test parallel get and set flags that may involve memory ordering.
            var ht = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            var name = ht.AddName(R("a", "b", "c", "d.cpp"));
            var flags = new HashSet<HierarchicalNameTable.NameFlags>(new[] {
                HierarchicalNameTable.NameFlags.Container,
                HierarchicalNameTable.NameFlags.Marked,
                HierarchicalNameTable.NameFlags.Root,
                HierarchicalNameTable.NameFlags.Sealed });

            foreach (var flag in flags)
            {
                ht.SetFlags(name, flag);
            }

            var allFlags = flags.Aggregate((f1, f2) => f1 | f2);
            XAssert.AreEqual(allFlags, ht.GetContainerAndFlags(name).Item2 & allFlags);

            foreach (var flag in flags)
            {
                tasks.Add(Task.Run(() =>
                {
                    var otherFlags = HierarchicalNameTable.NameFlags.None;
                    foreach (var of in flags)
                    {
                        if (of != flag)
                        {
                            otherFlags |= of;
                        }
                    }

                    bool clear = false;

                    for (int i = 0; i < 10000; ++i)
                    {
                        ht.SetFlags(name, flag, clear: clear);
                        XAssert.AreEqual(clear, (ht.GetContainerAndFlags(name).Item2 & flag) == 0);
                        clear = !clear;
                    }

                    XAssert.AreEqual(!clear, (ht.GetContainerAndFlags(name).Item2 & flag) == 0);
                }));
            }

            return Task.WhenAll(tasks);
        }

        [Fact]
        public void GetAndSetExtendedFlags()
        {
            var ht = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId tag1 = ht.AddName(R("c", "tag1"));
            HierarchicalNameId tag2 = ht.AddName(R("c", "tag2"));

            XAssert.IsTrue(ht.SetExtendedFlags(tag2, HierarchicalNameTable.ExtendedNameFlags.Flag1));

            XAssert.IsTrue(ht.SetExtendedFlags(tag1, HierarchicalNameTable.ExtendedNameFlags.Flag1));
            XAssert.IsFalse(ht.SetExtendedFlags(tag1, HierarchicalNameTable.ExtendedNameFlags.Flag1));
            XAssert.AreEqual(HierarchicalNameTable.ExtendedNameFlags.Flag1, ht.GetExtendedFlags(tag1));

            XAssert.IsTrue(ht.SetExtendedFlags(tag1, HierarchicalNameTable.ExtendedNameFlags.Flag3));
            XAssert.AreEqual(HierarchicalNameTable.ExtendedNameFlags.Flag1 | HierarchicalNameTable.ExtendedNameFlags.Flag3, ht.GetExtendedFlags(tag1));

            XAssert.IsTrue(ht.SetExtendedFlags(tag1, HierarchicalNameTable.ExtendedNameFlags.Flag3, clear: true));
            XAssert.AreEqual(HierarchicalNameTable.ExtendedNameFlags.Flag1, ht.GetExtendedFlags(tag1));

            XAssert.IsFalse(ht.SetExtendedFlags(tag1, HierarchicalNameTable.ExtendedNameFlags.Flag1 | HierarchicalNameTable.ExtendedNameFlags.Flag3, clear: true));
            XAssert.AreEqual(HierarchicalNameTable.ExtendedNameFlags.None, ht.GetExtendedFlags(tag1));

            // tag2 should be the same as before
            XAssert.AreEqual(HierarchicalNameTable.ExtendedNameFlags.Flag1, ht.GetExtendedFlags(tag2));
        }

        [Fact]
        public Task ConcurrentGetAndSetExtendedFlags()
        {
            // This test parallel get and set flags that may involve memory ordering.
            var ht = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            var name = ht.AddName(R("a", "b", "c", "d.cpp"));
            var flags = new HashSet<HierarchicalNameTable.ExtendedNameFlags>(new[] {
                HierarchicalNameTable.ExtendedNameFlags.Flag1,
                HierarchicalNameTable.ExtendedNameFlags.Flag2,
                HierarchicalNameTable.ExtendedNameFlags.Flag3,
                HierarchicalNameTable.ExtendedNameFlags.Flag4 });

            var tasks = new List<Task>();

            foreach (var flag in flags)
            {
                ht.SetExtendedFlags(name, flag);
            }

            var allFlags = flags.Aggregate((f1, f2) => f1 | f2);
            XAssert.AreEqual(allFlags, ht.GetExtendedFlags(name) & allFlags);

            foreach (var flag in flags)
            {
                tasks.Add(Task.Run(() =>
                {
                    var otherFlags = HierarchicalNameTable.ExtendedNameFlags.None;
                    foreach (var of in flags)
                    {
                        if (of != flag)
                        {
                            otherFlags |= of;
                        }
                    }

                    bool clear = false;

                    for (int i = 0; i < 10000; ++i)
                    {
                        ht.SetExtendedFlags(name, flag, clear: clear);
                        XAssert.AreEqual(clear, (ht.GetExtendedFlags(name) & flag) == 0);
                        clear = !clear;
                    }

                    XAssert.AreEqual(!clear, (ht.GetExtendedFlags(name) & flag) == 0);
                }));
            }

            return Task.WhenAll(tasks);
        }

        [Fact]
        public void EnumeratingBottomUpWithFlagFilter()
        {
            var ht = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId tag1 = ht.AddName(R("c", "tag1"));
            HierarchicalNameId tag2 = ht.AddName(R("c", "tag1", "notag", "tag2"));
            HierarchicalNameId wrongTag = ht.AddName(R("c", "tag1", "notag", "tag2", "wrongtag"));
            HierarchicalNameId leafTag = ht.AddName(R("c", "tag1", "notag", "tag2", "wrongtag", "leaftag"));

            ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Marked);
            ht.SetFlags(tag2, HierarchicalNameTable.NameFlags.Marked);
            ht.SetFlags(wrongTag, HierarchicalNameTable.NameFlags.Root);
            ht.SetFlags(leafTag, HierarchicalNameTable.NameFlags.Marked);

            Assert.Equal(
                ht.EnumerateHierarchyBottomUp(leafTag, HierarchicalNameTable.NameFlags.Marked).ToArray(),
                new[]
                {
                    leafTag,
                    tag2,
                    tag1,
                });
        }

        [Fact]
        public void EnumeratingImmediateChildren()
        {
            var ht = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId rootChild1 = ht.AddName(R("root", "rootChild1"));
            HierarchicalNameId otherRoot = ht.AddName(R("otherRoot"));
            HierarchicalNameId rootChild2 = ht.AddName(R("root", "rootChild2"));
            HierarchicalNameId rootGrandchild1 = ht.AddName(R("root", "rootChild1", "grandchild1"));
            HierarchicalNameId rootGrandchild2 = ht.AddName(R("root", "rootChild2", "grandchild2"));
            HierarchicalNameId rootGrandchild3 = ht.AddName(R("root", "rootChild2", "grandchild3"));
            HierarchicalNameId root = ht.AddName(R("root"));

            XAssert.AreEqual(0, ht.EnumerateImmediateChildren(otherRoot).Count());
            Assert.Equal(
                ht.EnumerateImmediateChildren(root).ToArray(),
                new[]
                {
                    // Siblings are in reverse addition order.
                    rootChild2,
                    rootChild1,
                });
            Assert.Equal(
                ht.EnumerateImmediateChildren(rootChild1).ToArray(),
                new[]
                {
                    rootGrandchild1
                });
            Assert.Equal(
                ht.EnumerateImmediateChildren(rootChild2).ToArray(),
                new[]
                {
                    rootGrandchild3,
                    rootGrandchild2,
                });
            XAssert.AreEqual(0, ht.EnumerateImmediateChildren(rootGrandchild2).Count());
        }

        [Fact]
        public void EnumeratingDescendants()
        {
            var ht = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId rootChild1 = ht.AddName(R("root","rootChild1"));
            HierarchicalNameId otherRoot = ht.AddName(R("otherRoot"));
            HierarchicalNameId rootChild2 = ht.AddName(R("root","rootChild2"));
            HierarchicalNameId rootGrandchild1 = ht.AddName(R("root","rootChild1","grandchild1"));
            HierarchicalNameId rootGrandchild2 = ht.AddName(R("root","rootChild2","grandchild2"));
            HierarchicalNameId rootGrandchild3 = ht.AddName(R("root","rootChild2","grandchild3"));
            HierarchicalNameId root = ht.AddName(R("root"));

            XAssert.AreEqual(0, ht.EnumerateHierarchyTopDown(otherRoot).Count());
            Assert.Equal(
                ht.EnumerateHierarchyTopDown(root).ToArray(),
                new[]
                {
                    // Note that the we traverse depth first, and with siblings in reverse order of addition.
                    rootChild2,
                    rootGrandchild3,
                    rootGrandchild2,
                    rootChild1,
                    rootGrandchild1,
                });
            Assert.Equal(
                ht.EnumerateHierarchyTopDown(rootChild1).ToArray(),
                new[]
                {
                    rootGrandchild1
                });
            Assert.Equal(
                ht.EnumerateHierarchyTopDown(rootChild2).ToArray(),
                new[]
                {
                    rootGrandchild3,
                    rootGrandchild2,
                });
            XAssert.AreEqual(0, ht.EnumerateHierarchyTopDown(rootGrandchild2).Count());
        }

        [Fact]
        public void SetFlags()
        {
            var ht = new HierarchicalNameTable(new StringTable(), true, Path.DirectorySeparatorChar);
            HierarchicalNameId tag1 = ht.AddName(R("c", "tag1"));

            XAssert.IsTrue(ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Marked));
            XAssert.IsFalse(ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Marked));
            XAssert.IsTrue(ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Root));
            XAssert.IsFalse(ht.SetFlags(tag1, HierarchicalNameTable.NameFlags.Root));

            HierarchicalNameId tag2 = ht.AddName(R("c", "tag2"));
            XAssert.IsTrue(ht.SetFlags(tag2, HierarchicalNameTable.NameFlags.Marked | HierarchicalNameTable.NameFlags.Root));
            XAssert.IsFalse(ht.SetFlags(tag2, HierarchicalNameTable.NameFlags.Marked));
            XAssert.IsFalse(ht.SetFlags(tag2, HierarchicalNameTable.NameFlags.Root));
        }

        [Fact]
        public void ExpandedNameComparer()
        {
            CheckExpandedNameComparer(
                R("X"),
                R("x"),
                R("A"),
                R("a"));

            CheckExpandedNameComparer(
                R("x","a","b"),
                R("x","a","b","c"),
                R("x","a","c"));

            CheckExpandedNameComparer(
                R("x","a","b"),
                R("x","a","b","c"),
                R("x","a","c"));

            CheckExpandedNameComparer(
                R("X","a","b"),
                R("x","a","b","c"),
                R("X","a","c"));

            CheckExpandedNameComparer(
                R("x","a","b"),
                R("X","a","b","c"),
                R("x","a","c"));

            CheckExpandedNameComparer(
                R("a","a","b"),
                R("b","a","b"),
                R("c","a","b"));

            CheckExpandedNameComparer(
                R("a","c","d"),
                R("B","c","d"));

            CheckExpandedNameComparer(
                R("a","C","d"),
                R("a","c","d"));
        }

        private void CheckExpandedNameComparer(params string[] names)
        {
            CheckExpandedNameComparerWithCaseSensitivity(true, names);
            CheckExpandedNameComparerWithCaseSensitivity(false, names);
        }

        private void CheckExpandedNameComparerWithCaseSensitivity(bool ignoreCase, params string[] names)
        {
            // For all-pairs in 'names', verifies the expanded name comparer agrees with expanding the names and then comparing.
            var ht = new HierarchicalNameTable(new StringTable(), ignoreCase, Path.DirectorySeparatorChar);

            IComparer<string> stringComparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            IComparer<HierarchicalNameId> nameComparer = ht.ExpandedNameComparer;

            HierarchicalNameId[] nameIds = new HierarchicalNameId[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                nameIds[i] = ht.AddName(names[i]);
            }

            for (int i = 0; i < names.Length; i++)
            {
                for (int j = 0; i < names.Length; i++)
                {
                    string expandedI = ht.ExpandName(nameIds[i]);
                    string expandedJ = ht.ExpandName(nameIds[j]);
                    int expected = stringComparer.Compare(expandedI, expandedJ);
                    int actual = nameComparer.Compare(nameIds[i], nameIds[j]);

                    if (expected < 0)
                    {
                        XAssert.IsTrue(actual < 0, "Case-sensitive: {3} ; Result {0}, expecting {1} < {2}", actual, names[i], names[j], !ignoreCase);
                    }
                    else if (expected > 0)
                    {
                        XAssert.IsTrue(actual > 0, "Case-sensitive: {3} ; Result {0}, expecting {1} > {2}", actual, names[i], names[j], !ignoreCase);
                    }
                    else
                    {
                        XAssert.IsTrue(actual == 0, "Case-sensitive: {3} ; Result {0}, expecting {1} == {2}", actual, names[i], names[j], !ignoreCase);
                    }
                }
            }
        }
    }

    internal static class Extensions
    {
        public static bool TryGetName(this HierarchicalNameTable table, string name, out HierarchicalNameId hierarchicalNameId)
        {
            Contract.Requires(name != null);
            Contract.Ensures(Contract.Result<bool>() == (Contract.ValueAtReturn(out hierarchicalNameId) != HierarchicalNameId.Invalid));

            string[] components = GetComponents(table, name);

            var componentIds = new StringId[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                componentIds[i] = table.StringTable.AddString(components[i]);
            }

            return table.TryGetName(componentIds, out hierarchicalNameId);
        }

        public static HierarchicalNameId AddName(this HierarchicalNameTable table, string name)
        {
            Contract.Requires(name != null);
            Contract.Ensures(Contract.Result<HierarchicalNameId>() != HierarchicalNameId.Invalid);

            string[] components = GetComponents(table, name);
            var componentIds = new StringId[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                componentIds[i] = table.StringTable.AddString(components[i]);
            }

            return table.AddComponents(HierarchicalNameId.Invalid, componentIds);
        }

        private static string[] GetComponents(this HierarchicalNameTable table, string name)
        {
            Contract.Requires(name != null);
            Contract.Ensures(Contract.Result<string[]>() != null);
            Contract.Ensures(Contract.ForAll(Contract.Result<string[]>(), c => !string.IsNullOrEmpty(c)));

            var components = name.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            // Make sure to add the UnixPathRootSentinel to signal the root node existence on Unix
            if (OperatingSystemHelper.IsUnixOS)
            {
                List<string> pathList = new List<string>(components);
                pathList.Insert(0, HierarchicalNameTable.UnixPathRootSentinel);
                components = pathList.ToArray();
            }

            return components;
        }
    }
}
