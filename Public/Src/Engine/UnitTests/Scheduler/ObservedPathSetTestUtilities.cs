// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.Scheduler
{
    public static class ObservedPathSetTestUtilities
    {
        public static void AssertPathSetContainsDuplicates(ObservedPathSet pathSet)
        {
            bool foundDuplicate = false;
            for (int i = 1; i < pathSet.Paths.Length; i++)
            {
                int cmp = pathSet.Paths.Comparer.Compare(pathSet.Paths[i], pathSet.Paths[i - 1]);
                XAssert.IsTrue(
                    cmp >= 0,
                    "Path set must contain unique items in a sorted order");

                foundDuplicate |= cmp == 0;
            }

            XAssert.IsTrue(foundDuplicate, "Expected at least one duplicate");
        }

        public static void AssertPathSetDoesNotContainDuplicates(ObservedPathSet pathSet)
        {
            for (int i = 1; i < pathSet.Paths.Length; i++)
            {
                XAssert.IsTrue(
                    pathSet.Paths.Comparer.Compare(pathSet.Paths[i], pathSet.Paths[i - 1]) > 0,
                    "Path set must contain unique items in a sorted order");
            }
        }

        public static void AssertPathSetsEquivalent(ObservedPathSet a, ObservedPathSet b)
        {
            List<AbsolutePath> aList = RemoveDuplicates(a);
            List<AbsolutePath> bList = RemoveDuplicates(b);

            XAssert.AreEqual(aList.Count, bList.Count);
            for (int i = 0; i < aList.Count; i++)
            {
                XAssert.AreEqual(aList[i], bList[i]);
            }
        }

        public static List<AbsolutePath> RemoveDuplicates(ObservedPathSet pathSet)
        {
            var paths = new List<AbsolutePath>();
            for (int i = 0; i < pathSet.Paths.Length; i++)
            {
                while (i + 1 < pathSet.Paths.Length && pathSet.Paths[i + 1].Path == pathSet.Paths[i].Path)
                {
                    i++;
                }

                paths.Add(pathSet.Paths[i].Path);
            }

            return paths;
        }

        public static ObservedPathSet CreatePathSet(PathTable pathTable, params string[] paths)
        {
            AbsolutePath[] pathIds = new AbsolutePath[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                pathIds[i] = AbsolutePath.Create(pathTable, paths[i]);
            }

            SortedReadOnlyArray<AbsolutePath, PathTable.ExpandedAbsolutePathComparer> sortedPathIds =
                SortedReadOnlyArray<AbsolutePath, PathTable.ExpandedAbsolutePathComparer>.SortUnsafe(
                    pathIds,
                    pathTable.ExpandedPathComparer);

            return CreatePathSet(pathTable, pathIds);
        }

        public static ObservedPathSet CreatePathSet(PathTable pathTable, params AbsolutePath[] paths)
        {
            ObservedPathEntry[] entries = paths.Select(p => new ObservedPathEntry(p, false, false, false, null, false)).ToArray();

            SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer> sortedPathIds =
                SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer>.SortUnsafe(
                    entries,
                    new ObservedPathEntryExpandedPathComparer(pathTable.ExpandedPathComparer));

            var emptyObservedAccessFileNames = SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>.FromSortedArrayUnsafe(
                ReadOnlyArray<StringId>.Empty,
                new CaseInsensitiveStringIdComparer(pathTable.StringTable));

            return new ObservedPathSet(
                sortedPathIds, 
                emptyObservedAccessFileNames, 
                new UnsafeOptions(UnsafeOptions.SafeConfigurationValues, ContentHashingUtilities.CreateRandom()));
        }
    }
}
