// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for ConcurrentBigSet
    /// </summary>
    public class ScopedReferenceMapTests : XunitBuildXLTest
    {
        public ScopedReferenceMapTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestScopes()
        {
            int length = 10000;
            var comparer = new CollisionComparer();
            ScopeReferenceMapVerifier map = new ScopeReferenceMapVerifier(length);

            // Verify setting bits
            For(20, i =>
            {
                For(length, j =>
                {
                    using (var scope = map.OpenScope(j))
                    {
                        // Verify that the scope retains its key in the presence of concurrent updates
                        XAssert.IsTrue(comparer.Equals(j, scope.Key));

                        // Verify that the scope retains its value in the presence of concurrent updates
                        XAssert.AreEqual(GetMappedKey(j).ToString(), scope.Value);

                        // Verify that map reports at least one scope is open since
                        // the current scope is open
                        XAssert.IsTrue(map.OpenScopeCount > 0);
                    }
                }, true);
            }, true);

            // Verify that all scopes get cleaned up.
            XAssert.AreEqual(0, map.OpenScopeCount);
        }

        private class CollisionComparer : EqualityComparer<int>
        {
            public override bool Equals(int x, int y)
            {
                return GetMappedKey(x) == GetMappedKey(y);
            }

            public override int GetHashCode(int obj)
            {
                return GetMappedKey(obj).GetHashCode();
            }
        }

        /// <summary>
        /// Map the key to a lower range to ensure collisions
        /// </summary>
        private static int GetMappedKey(int key)
        {
            return key % 21;
        }

        private static void For(int count, Action<int> action, bool parallel)
        {
            if (parallel)
            {
                Parallel.For(0, count, action);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    action(i);
                }
            }
        }

        private class ScopeReferenceMapVerifier : ScopedReferenceMap<int, string>
        {
            private int[] m_refCounts;

            public ScopeReferenceMapVerifier(int max)
                : base(new CollisionComparer())
            {
                m_refCounts = new int[max];
            }

            protected override string CreateValue(int key)
            {
                // Verify that value is only created once for a particular key
                // even if multiple scopes are opened on the key
                XAssert.AreEqual(1, Interlocked.Increment(ref m_refCounts[GetMappedKey(key)]), "Value was created more than once.");
                return GetMappedKey(key).ToString();
            }

            protected override void ReleaseValue(int key, string value)
            {
                // Verify that value is only released once for a particular key
                // even if multiple scopes are closed on the key
                XAssert.AreEqual(0, Interlocked.Decrement(ref m_refCounts[GetMappedKey(key)]), "Value was released more than once.");
            }
        }
    }
}
