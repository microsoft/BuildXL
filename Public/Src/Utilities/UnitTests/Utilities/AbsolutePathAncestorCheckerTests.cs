// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public sealed class AbsolutePathAncestorCheckerTests : XunitBuildXLTest
    {
        public AbsolutePathAncestorCheckerTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void AncestorsAreProperlyChecked()
        {
            AbsolutePathAncestorChecker checker = new AbsolutePathAncestorChecker();
            PathTable pt = new PathTable();

            var pathToSomething = AbsolutePath.Create(pt, @"C:\path\to\something");
            var pathToSomethingElse = AbsolutePath.Create(pt, @"C:\path\to\something-else");

            checker.AddPath(pathToSomething);
            checker.AddPath(pathToSomethingElse);
            
            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomething.Combine(pt, "descendant")));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomething.Combine(pt, RelativePath.Create(pt.StringTable, @"a\deeper\descendant"))));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomethingElse));

            XAssert.IsFalse(checker.HasKnownAncestor(pt, AbsolutePath.Create(pt, @"C:\path\to")));
            XAssert.IsFalse(checker.HasKnownAncestor(pt, AbsolutePath.Create(pt, @"G:\unrelated\path")));
        }

        [Fact]
        public void ClearWorksAsExpected()
        {
            AbsolutePathAncestorChecker checker = new AbsolutePathAncestorChecker();
            PathTable pt = new PathTable();

            var pathToSomething = AbsolutePath.Create(pt, @"C:\path\to\something");

            checker.AddPath(pathToSomething);

            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomething));
            XAssert.IsTrue(checker.HasKnownAncestor(pt, pathToSomething.Combine(pt, "descendant")));

            checker.Clear();

            XAssert.IsFalse(checker.HasKnownAncestor(pt, pathToSomething));
            XAssert.IsFalse(checker.HasKnownAncestor(pt, pathToSomething.Combine(pt, "descendant")));
        }
    }
}
