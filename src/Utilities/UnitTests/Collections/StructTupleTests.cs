// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;                    

namespace Test.BuildXL.Utilities
{
    public class StructTupleTests
    {
        [Fact]
        public void StructTupleEquality()
        {
            StructTester.TestEquality(
                baseValue: StructTuple.Create(1, 2),
                equalValue: StructTuple.Create(1, 2),
                notEqualValues: new[]
                                {
                                    StructTuple.Create(1, 3),
                                    StructTuple.Create(2, 2),
                                    StructTuple.Create(2, 1)
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);
        }

        [Fact]
        public void StructTupleEqualityWithReferenceTypes()
        {
            var o1 = new TestEquatable(1);
            var o2 = new TestEquatable(2);
            var o3 = new TestEquatable(3);

            StructTester.TestEquality(
                baseValue: StructTuple.Create(o1, o2),
                equalValue: StructTuple.Create(o1, o2),
                notEqualValues: new[]
                                {
                                    StructTuple.Create(o1, o3),
                                    StructTuple.Create(o2, o2),
                                    StructTuple.Create(o2, o1),
                                    StructTuple.Create<TestEquatable, TestEquatable>(null, o2),
                                    StructTuple.Create<TestEquatable, TestEquatable>(o1, null)
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);

            StructTester.TestEquality(
                baseValue: StructTuple.Create(o1, o2),
                equalValue: StructTuple.Create(new TestEquatable(1), new TestEquatable(2)),
                notEqualValues: new[]
                                            {
                                                StructTuple.Create(new TestEquatable(1), new TestEquatable(3)),
                                                StructTuple.Create(new TestEquatable(2), new TestEquatable(2)),
                                                StructTuple.Create(new TestEquatable(2), new TestEquatable(1)),
                                                StructTuple.Create<TestEquatable, TestEquatable>(null, null)
                                            },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);
        }

        [Fact]
        public void StructTupleEqualityWithReferenceTypesInEquatableWrapper()
        {
            var o1 = new TestNonEquatable(1);
            var o2 = new TestNonEquatable(2);
            var o3 = new TestNonEquatable(3);

            StructTester.TestEquality(
                baseValue: StructTuple.Create(EquatableClass.Create(o1), EquatableClass.Create(o2)),
                equalValue: StructTuple.Create(EquatableClass.Create(o1), EquatableClass.Create(o2)),
                notEqualValues: new[]
                                {
                                    StructTuple.Create(EquatableClass.Create(o1), EquatableClass.Create(o3)),
                                    StructTuple.Create(EquatableClass.Create(o2), EquatableClass.Create(o2)),
                                    StructTuple.Create(EquatableClass.Create(o2), EquatableClass.Create(o1)),
                                    StructTuple.Create<EquatableClass<TestNonEquatable>, EquatableClass<TestNonEquatable>>(null, o2),
                                    StructTuple.Create<EquatableClass<TestNonEquatable>, EquatableClass<TestNonEquatable>>(o1, null)
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);

            StructTester.TestEquality(
                baseValue: StructTuple.Create<EquatableClass<TestNonEquatable>, EquatableClass<TestNonEquatable>>(o1, o2),
                equalValue: StructTuple.Create<EquatableClass<TestNonEquatable>, EquatableClass<TestNonEquatable>>(new TestNonEquatable(1), new TestNonEquatable(2)),
                notEqualValues: new[]
                                            {
                                                StructTuple.Create<EquatableClass<TestNonEquatable>, EquatableClass<TestNonEquatable>>(new TestNonEquatable(1), new TestNonEquatable(3)),
                                                StructTuple.Create<EquatableClass<TestNonEquatable>, EquatableClass<TestNonEquatable>>(new TestNonEquatable(2), new TestNonEquatable(2)),
                                                StructTuple.Create<EquatableClass<TestNonEquatable>, EquatableClass<TestNonEquatable>>(new TestNonEquatable(2), new TestNonEquatable(1)),
                                                StructTuple.Create<EquatableClass<TestNonEquatable>, EquatableClass<TestNonEquatable>>(null, null)
                                            },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);
        }

        [Fact]
        public void StructTupleEqualityWithEnumInEquatableWrapper()
        {
            StructTester.TestEquality(
                baseValue: StructTuple.Create<EquatableEnum<TestEnum>, EquatableEnum<TestEnum>>(TestEnum.A, TestEnum.B),
                equalValue: StructTuple.Create<EquatableEnum<TestEnum>, EquatableEnum<TestEnum>>(TestEnum.A, TestEnum.B),
                notEqualValues: new[]
                                {
                                    StructTuple.Create<EquatableEnum<TestEnum>, EquatableEnum<TestEnum>>(TestEnum.B, TestEnum.A),
                                    StructTuple.Create<EquatableEnum<TestEnum>, EquatableEnum<TestEnum>>(TestEnum.A, TestEnum.A),
                                    StructTuple.Create<EquatableEnum<TestEnum>, EquatableEnum<TestEnum>>(TestEnum.C, TestEnum.C)
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);
        }

        [Fact]
        public void Constructor()
        {
            StructTuple<int, int> v = StructTuple.Create(1, 2);
            XAssert.AreEqual(1, v.Item1);
            XAssert.AreEqual(2, v.Item2);
        }

        private enum TestEnum
        {
            A,
            B,
            C
        }

        private class TestEquatable : IEquatable<TestEquatable>
        {
            private readonly int m_value;

            public TestEquatable(int value)
            {
                m_value = value;
            }

            public override int GetHashCode()
            {
                return m_value.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                return obj is TestEquatable && Equals((TestEquatable)obj);
            }

            public bool Equals(TestEquatable other)
            {
                return other != null && other.m_value == m_value;
            }
        }

        private class TestNonEquatable
        {
            private readonly int m_value;

            public TestNonEquatable(int value)
            {
                m_value = value;
            }

            public override int GetHashCode()
            {
                return m_value.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                return obj is TestNonEquatable && ((TestNonEquatable)obj).m_value == m_value;
            }
        }
    }
}
