// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

            for (int i = 1; i < pathSet.ObservedAccessedFileNames.Length; i++)
            {
                int cmp = pathSet.ObservedAccessedFileNames.Comparer.Compare(pathSet.ObservedAccessedFileNames[i], pathSet.ObservedAccessedFileNames[i - 1]);
                XAssert.IsTrue(
                    cmp >= 0,
                    "Observed accessed file names must contain unique items in a sorted order");

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

            for (int i = 1; i < pathSet.ObservedAccessedFileNames.Length; i++)
            {
                XAssert.IsTrue(
                    pathSet.ObservedAccessedFileNames.Comparer.Compare(pathSet.ObservedAccessedFileNames[i], pathSet.ObservedAccessedFileNames[i - 1]) > 0,
                    "Observed accessed file names must contain unique items in a sorted order");
            }
        }

        public static void AssertPathSetsEquivalent(ObservedPathSet a, ObservedPathSet b)
        {
            List<AbsolutePath> aPaths = RemoveDuplicates(a.Paths);
            List<AbsolutePath> bPaths = RemoveDuplicates(b.Paths);

            XAssert.AreEqual(aPaths.Count, bPaths.Count);
            for (int i = 0; i < aPaths.Count; i++)
            {
                XAssert.AreEqual(aPaths[i], bPaths[i]);
            }

            List<StringId> aFileNames = RemoveDuplicates(a.ObservedAccessedFileNames);
            List<StringId> bFileNames = RemoveDuplicates(b.ObservedAccessedFileNames);

            XAssert.AreEqual(aFileNames.Count, bFileNames.Count);
            for (int i = 0; i < aFileNames.Count; i++)
            {
                XAssert.IsTrue(a.ObservedAccessedFileNames.Comparer.Compare(aFileNames[i], bFileNames[i]) == 0);
            }
        }

        public static List<AbsolutePath> RemoveDuplicates(SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer> paths)
            => RemoveDuplicates(
                paths,
                comparer: (a, b) => a.Path == b.Path,
                selector: v => v.Path);

        public static List<StringId> RemoveDuplicates(SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> observedAccessedFileNames)
            => RemoveDuplicates(
                observedAccessedFileNames,
                comparer: (a, b) => !OperatingSystemHelper.IsUnixOS && observedAccessedFileNames.Comparer.Compare(a, b) == 0 || a == b,
                selector: v => v);

        public static List<TValue2> RemoveDuplicates<TValue, TValue2, TComparer>(
            SortedReadOnlyArray<TValue, TComparer> values,
            Func<TValue, TValue, bool> comparer,
            Func<TValue, TValue2> selector)
            where TComparer : class, IComparer<TValue>
        {
            var uniqueEntries = new List<TValue2>();
            for (int i = 0; i < values.Length; i++)
            {
                while (i + 1 < values.Length && comparer(values[i + 1], values[i]))
                {
                    i++;
                }

                uniqueEntries.Add(selector(values[i]));
            }

            return uniqueEntries;
        }

        public static ObservedPathSet CreatePathSet(PathTable pathTable, params string[] paths)
            => CreatePathSet(pathTable, Array.Empty<string>(), paths);

        public static ObservedPathSet CreatePathSet(PathTable pathTable, string[] observedAccessedFileNames, params string[] paths)
        {
            AbsolutePath[] pathIds = new AbsolutePath[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                pathIds[i] = AbsolutePath.Create(pathTable, paths[i]);
            }

            StringId[] stringIds = new StringId[observedAccessedFileNames.Length];
            for (int i = 0; i < stringIds.Length; i++)
            {
                stringIds[i] = StringId.Create(pathTable.StringTable, observedAccessedFileNames[i]);
            }

            return CreatePathSet(pathTable, stringIds, pathIds);
        }

        public static ObservedPathSet CreatePathSet(PathTable pathTable, params AbsolutePath[] paths)
             => CreatePathSet(pathTable, Array.Empty<StringId>(), paths);

        public static ObservedPathSet CreatePathSet(PathTable pathTable, StringId[] observedNames, AbsolutePath[] paths)
        {
            ObservedPathEntry[] entries = paths.Select(p => new ObservedPathEntry(p, false, false, false, null, false)).ToArray();

            SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer> sortedPathIds =
                SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer>.SortUnsafe(
                    entries,
                    new ObservedPathEntryExpandedPathComparer(pathTable.ExpandedPathComparer));

            SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> observedAccessFileNames =
                SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>.SortUnsafe(
                    observedNames,
                    new CaseInsensitiveStringIdComparer(pathTable.StringTable));

            return new ObservedPathSet(
                sortedPathIds,
                observedAccessFileNames,
                new UnsafeOptions(UnsafeOptions.SafeConfigurationValues, new PreserveOutputsInfo(ContentHashingUtilities.CreateRandom(), 0)));
        }
    }
}
