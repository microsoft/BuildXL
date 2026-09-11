// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    [TestClassIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
    public sealed class AbsolutePathAncestorCheckerTests : XunitBuildXLTest
    {
        public AbsolutePathAncestorCheckerTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void EmptyCheckerHasNoKnownAncestors()
        {
            var checker = new AbsolutePathAncestorChecker();
            var pt = new PathTable();
            var pathToSomething = AbsolutePath.Create(pt, X(@"/c/path/to/something"));

            XAssert.IsFalse(checker.HasKnownAncestor(pt, pathToSomething));
            XAssert.IsFalse(checker.HasKnownAncestor(pt, pathToSomething.Combine(pt, "descendant")));
        }

        [Fact]
        public void ExactMatchIsRecognized()
        {
            var checker = new AbsolutePathAncestorChecker();
            var pt = new PathTable();
            var pathToSomething = AbsolutePath.Create(pt, X(@"/c/path/to/something"));

            checker.AddPath(pathToSomething);

            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomething));
        }

        [Fact]
        public void DescendantIsRecognized()
        {
            var checker = new AbsolutePathAncestorChecker();
            var pt = new PathTable();
            var pathToSomething = AbsolutePath.Create(pt, X(@"/c/path/to/something"));

            checker.AddPath(pathToSomething);

            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomething.Combine(pt, "descendant")));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomething.Combine(pt, RelativePath.Create(pt.StringTable, @"a\deeper\descendant"))));
        }

        [Fact]
        public void UnrelatedPathIsRejected()
        {
            var checker = new AbsolutePathAncestorChecker();
            var pt = new PathTable();
            var pathToSomething = AbsolutePath.Create(pt, X(@"/c/path/to/something"));

            checker.AddPath(pathToSomething);

            XAssert.IsFalse(checker.HasKnownAncestor(pt, AbsolutePath.Create(pt, X(@"/c/path/to"))));
            XAssert.IsFalse(checker.HasKnownAncestor(pt, AbsolutePath.Create(pt, X(@"/c/unrelated/path"))));
        }

        [Fact]
        public void RepeatedSiblingQueriesRemainCorrect()
        {
            var checker = new AbsolutePathAncestorChecker();
            var pt = new PathTable();
            var sharedAncestor = AbsolutePath.Create(pt, X(@"/c/path/to/shared"));
            var siblingParent = sharedAncestor.Combine(pt, RelativePath.Create(pt.StringTable, @"nested\parent"));

            checker.AddPath(sharedAncestor);

            XAssert.IsTrue(checker.HasKnownAncestor(pt, siblingParent.Combine(pt, "sibling-one")));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, siblingParent.Combine(pt, "sibling-two")));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, siblingParent.Combine(pt, RelativePath.Create(pt.StringTable, @"deeper\sibling-three"))));
        }

        [Fact]
        public void AddingKnownAncestorInvalidatesCachedNegativeQueries()
        {
            var checker = new AbsolutePathAncestorChecker();
            var pt = new PathTable();
            var unrelatedKnownPath = AbsolutePath.Create(pt, X(@"/d/other/path"));
            var newKnownAncestor = AbsolutePath.Create(pt, X(@"/c/path/to/something"));
            var descendant = newKnownAncestor.Combine(pt, RelativePath.Create(pt.StringTable, @"nested\descendant"));

            checker.AddPath(unrelatedKnownPath);

            XAssert.IsFalse(checker.HasKnownAncestor(pt, descendant));

            checker.AddPath(newKnownAncestor);

            XAssert.IsTrue(checker.HasKnownAncestor(pt, descendant));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, newKnownAncestor.Combine(pt, "sibling")));
        }

        [Fact]
        public void AddingMoreSpecificPathAfterQueriesRemainsCorrect()
        {
            var checker = new AbsolutePathAncestorChecker();
            var pt = new PathTable();
            var broadAncestor = AbsolutePath.Create(pt, X(@"/c/path/to/shared"));
            var specificAncestor = broadAncestor.Combine(pt, RelativePath.Create(pt.StringTable, @"nested\deeper"));

            checker.AddPath(broadAncestor);

            XAssert.IsTrue(checker.HasKnownAncestor(pt, broadAncestor.Combine(pt, RelativePath.Create(pt.StringTable, @"nested\first-child"))));
            XAssert.IsFalse(checker.HasKnownAncestor(pt, AbsolutePath.Create(pt, X(@"/c/unrelated/path"))));

            checker.AddPath(specificAncestor);

            XAssert.IsTrue(checker.HasKnownAncestor(pt, specificAncestor));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, specificAncestor.Combine(pt, "descendant")));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, broadAncestor.Combine(pt, "other-child")));
        }

        [Fact]
        public void ClearWorksAsExpected()
        {
            var checker = new AbsolutePathAncestorChecker();
            var pt = new PathTable();

            var pathToSomething = AbsolutePath.Create(pt, X(@"/c/path/to/something"));

            checker.AddPath(pathToSomething);

            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomething));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomething.Combine(pt, "descendant")));

            checker.Clear();

            XAssert.IsFalse(checker.HasKnownAncestor(pt, pathToSomething));
            XAssert.IsFalse(checker.HasKnownAncestor(pt, pathToSomething.Combine(pt, "descendant")));
        }

        [Fact]
        public void OversizedCheckerIsNotRetainedByPool()
        {
            const int maximumRetainedEntries = 4;
            var pool = new ObjectPool<AbsolutePathAncestorChecker>(
                () => new AbsolutePathAncestorChecker(),
                checker => checker.Clear(),
                sizeProvider: checker => checker.RetainedEntryCapacity,
                maximumRetainedSize: maximumRetainedEntries);
            var pt = new PathTable();
            var unrelatedKnownPath = AbsolutePath.Create(pt, X(@"/d/other/path"));
            var queriedPath = AbsolutePath.Create(pt, X(@"/c/path/with/several/parents/and/a/leaf"));
            AbsolutePathAncestorChecker oversizedChecker;

            using (var wrapper = pool.GetInstance())
            {
                oversizedChecker = wrapper.Instance;
                oversizedChecker.AddPath(unrelatedKnownPath);
                XAssert.IsFalse(oversizedChecker.HasKnownAncestor(pt, queriedPath));
                XAssert.IsTrue(oversizedChecker.RetainedEntryCapacity > maximumRetainedEntries);

#if NETCOREAPP
                // Invalidating the negative cache must not hide its allocated backing storage.
                oversizedChecker.AddPath(queriedPath);
                XAssert.IsTrue(oversizedChecker.RetainedEntryCapacity > maximumRetainedEntries);
#endif
            }

            XAssert.AreEqual(1, pool.OversizedObjectCount);
            using var replacementWrapper = pool.GetInstance();
            XAssert.AreNotSame(oversizedChecker, replacementWrapper.Instance);
            XAssert.AreEqual(0, replacementWrapper.Instance.RetainedEntryCapacity);
        }
    }
}
