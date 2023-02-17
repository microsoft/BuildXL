// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class DirectoryMembershipFingerprinterRuleSetTests : XunitBuildXLTest
    {
        private readonly BuildXLContext m_context;

        public DirectoryMembershipFingerprinterRuleSetTests(ITestOutputHelper output) : base(output)
        {
            m_context = BuildXLContext.CreateInstanceForTesting();
        }

        [Fact]
        public void TestNoRule()
        {
            var ruleSet = new DirectoryMembershipFingerprinterRuleSet(Array.Empty<DirectoryMembershipFingerprinterRule>());
            XAssert.IsFalse(ruleSet.TryGetRule(m_context.PathTable, APath("/X/FooDir"), out DirectoryMembershipFingerprinterRule rule));
            XAssert.IsNull(rule);
        }

        [Fact]
        public void TestNoMatchRule()
        {
            var ruleSet = new DirectoryMembershipFingerprinterRuleSet(new[]
            {
                new DirectoryMembershipFingerprinterRule("TestRule1", APath("/X/Dir1"), false, new[] { "foo1", "bar1" }, false),
                new DirectoryMembershipFingerprinterRule("TestRule11", APath("/X/Dir1/Dir11"), false, new[] { "foo11", "bar11" }, false),
                new DirectoryMembershipFingerprinterRule("TestRule2", APath("/X/Dir2"), false, new[] { "foo2", "bar2" }, true),
            });

            XAssert.IsFalse(ruleSet.TryGetRule(m_context.PathTable, APath("/X/Dir0/Dir01"), out DirectoryMembershipFingerprinterRule rule01));
            XAssert.IsNull(rule01);

            XAssert.IsFalse(ruleSet.TryGetRule(m_context.PathTable, APath("/X/Dir1/Dir12"), out DirectoryMembershipFingerprinterRule rule12));
            XAssert.IsNull(rule12);
        }

        [Fact]
        public void TestSingleMatchRule()
        {
            DirectoryMembershipFingerprinterRule expectedRule11;
            DirectoryMembershipFingerprinterRule expectedRule2;

            var ruleSet = new DirectoryMembershipFingerprinterRuleSet(new[]
            {
                new DirectoryMembershipFingerprinterRule("TestRule1", APath("/X/Dir1"), false, new[] { "foo1", "bar1" }, false),
                expectedRule11 = new DirectoryMembershipFingerprinterRule("TestRule11", APath("/X/Dir1/Dir11"), false, new[] { "foo11", "bar11" }, false),
                expectedRule2  = new DirectoryMembershipFingerprinterRule("TestRule2", APath("/X/Dir2"), false, new[] { "foo2", "bar2" }, true),
            });

            XAssert.IsTrue(ruleSet.TryGetRule(m_context.PathTable, APath("/X/Dir1/Dir11"), out DirectoryMembershipFingerprinterRule rule11));
            XAssert.IsNotNull(rule11);
            XAssert.AreEqual(expectedRule11, rule11);

            XAssert.IsTrue(ruleSet.TryGetRule(m_context.PathTable, APath("/X/Dir2/Dir22"), out DirectoryMembershipFingerprinterRule rule22));
            XAssert.IsNotNull(rule22);
            XAssert.AreEqual(expectedRule2, rule22);
        }

        [Fact]
        public void TestAggregateRules()
        {
            var ruleSet = new DirectoryMembershipFingerprinterRuleSet(new[]
            {
                new DirectoryMembershipFingerprinterRule("TestRuleA", APath("/X/Dir1/Dir2/Dir3/Dir4/Dir5"), false, new[] { "fooA", "barA" }, false),
                new DirectoryMembershipFingerprinterRule("TestRuleB", APath("/X/Dir1/Dir2/Dir3/Dir4"), false, new[] { "fooB", "barB" }, false),       // Not inherited due to non-recursive.
                new DirectoryMembershipFingerprinterRule("TestRuleC", APath("/X/Dir1/Dir2/Dir3"), false, new[] { "fooC", "barC" }, true),             // Inherited due to recursive.
                new DirectoryMembershipFingerprinterRule("TestRuleD", APath("/X/Dir1/Dir2"), true, null, true),                                       // Inherited due to recursive, disabling rule, but get overriden by TestRuleC.
                new DirectoryMembershipFingerprinterRule("TestRuleE", APath("/X/Dir1"), false, new[] { "fooE", "barE" }, true),                       // Not inherited, although recursive, but get overriden by TestRuleD.
            });

            XAssert.IsTrue(ruleSet.TryGetRule(m_context.PathTable, APath("/X/Dir1/Dir2/Dir3/Dir4/Dir5"), out DirectoryMembershipFingerprinterRule rule));
            XAssert.IsNotNull(rule);
            XAssert.IsFalse(rule.Recursive);
            XAssert.IsFalse(rule.DisableFilesystemEnumeration);
            XAssert.IsTrue(rule.FileIgnoreWildcards.ToHashSet().SetEquals(new[] { "fooA", "barA", "fooC", "barC" }));
        }

        [Fact]
        public void TestParentGetOverridden()
        {
            var parentRuleSet = new DirectoryMembershipFingerprinterRuleSet(new[]
            {
                new DirectoryMembershipFingerprinterRule("TestRuleF", APath("/X/Dir1/Dir2/Dir3/Dir4/Dir5"), false, new[] { "fooF", "barF" }, false),  // Will be overriden by the child rule set.
                new DirectoryMembershipFingerprinterRule("TestRuleG", APath("/X/Dir1/Dir2/Dir3"), false, new[] { "fooG", "barG" }, true)             // Will be inherited by the child rule set.
            });

            var ruleSet = new DirectoryMembershipFingerprinterRuleSet(new[]
            {
                new DirectoryMembershipFingerprinterRule("TestRuleA", APath("/X/Dir1/Dir2/Dir3/Dir4/Dir5"), false, new[] { "fooA", "barA" }, false),
                new DirectoryMembershipFingerprinterRule("TestRuleB", APath("/X/Dir1/Dir2/Dir3/Dir4"), false, new[] { "fooB", "barB" }, false),       // Not inherited due to non-recursive.
                new DirectoryMembershipFingerprinterRule("TestRuleD", APath("/X/Dir1/Dir2"), true, null, true),                                       // Inherited due to recursive, disabling rule, but get overriden by TestRuleC.
                new DirectoryMembershipFingerprinterRule("TestRuleE", APath("/X/Dir1"), false, new[] { "fooE", "barE" }, true),                       // Not inherited, although recursive, but get overriden by TestRuleD.
            },
            parentRuleSet);

            XAssert.IsTrue(ruleSet.TryGetRule(m_context.PathTable, APath("/X/Dir1/Dir2/Dir3/Dir4/Dir5"), out DirectoryMembershipFingerprinterRule rule));
            XAssert.IsNotNull(rule);
            XAssert.IsFalse(rule.Recursive);
            XAssert.IsFalse(rule.DisableFilesystemEnumeration);
            XAssert.IsTrue(rule.FileIgnoreWildcards.ToHashSet().SetEquals(new[] { "fooA", "barA", "fooG", "barG" }));
        }

        [Fact]
        public void TestDisableRule()
        {
            var ruleSet = new DirectoryMembershipFingerprinterRuleSet(new[]
            {
                new DirectoryMembershipFingerprinterRule("TestRuleB", APath("/X/Dir1/Dir2/Dir3/Dir4"), false, new[] { "fooB", "barB" }, false),       // Not inherited due to non-recursive.
                new DirectoryMembershipFingerprinterRule("TestRuleC", APath("/X/Dir1/Dir2/Dir3"), true, null, true),                                  // Inherited due to recursive, disabling rule.
                new DirectoryMembershipFingerprinterRule("TestRuleD", APath("/X/Dir1/Dir2"), false, new[] { "fooD", "barD" }, true), 
                new DirectoryMembershipFingerprinterRule("TestRuleE", APath("/X/Dir1"), false, new[] { "fooE", "barE" }, true),
            });

            XAssert.IsTrue(ruleSet.TryGetRule(m_context.PathTable, APath("/X/Dir1/Dir2/Dir3/Dir4/Dir5"), out DirectoryMembershipFingerprinterRule rule));
            XAssert.IsNotNull(rule);
            XAssert.IsTrue(rule.Recursive);
            XAssert.IsTrue(rule.DisableFilesystemEnumeration);
        }

        private AbsolutePath APath(string absolutePath) => AbsolutePath.Create(m_context.PathTable, X(absolutePath));
    }
}
