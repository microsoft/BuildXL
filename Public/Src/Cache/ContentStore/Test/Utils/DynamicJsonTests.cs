// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Utils;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Utils
{
    public class DynamicJsonTests
    {
        [Fact]
        public void RoundtripTest()
        {
            // This should even work with interfaces. Explicit cast to interface.
            ISomeInterface obj = new TheClass { Inner = new TheClass.InnerClass { Foo = "bar", Bar = long.MaxValue }, Value = true };
            var serialized = DynamicJson.Serialize(obj);
            var (deserialized, type) = DynamicJson.Deserialize<ISomeInterface>(serialized);

            type.Should().Be(typeof(TheClass));
            ((TheClass)deserialized).Inner.Foo.Should().Be(((TheClass)obj).Inner.Foo);
            ((TheClass)deserialized).Inner.Bar.Should().Be(((TheClass)obj).Inner.Bar);
            ((TheClass)deserialized).Value.Should().Be(((TheClass)obj).Value);
        }

        [Fact]
        public void DeserializeRejectsUnexpectedTypeBeforePopulatingIt()
        {
            var serialized = DynamicJson.Serialize(new UnexpectedType { Value = true });
            UnexpectedType.SetterCallCount = 0;

            Action deserialize = () => DynamicJson.Deserialize<ISomeInterface>(serialized);

            deserialize.Should().Throw<Exception>().WithMessage("*not assignable*");
            UnexpectedType.SetterCallCount.Should().Be(0);
        }

        public abstract class AbstractClass : ISomeInterface
        {
            public bool Value { get; set; }
        }

        public class TheClass : AbstractClass, ISomeInterface
        {
            public InnerClass Inner { get; set; }

            public class InnerClass
            {
                public string Foo { get; set; }
                public long Bar { get; set; }
            }
        }

        public interface ISomeInterface
        {

        }

        public class UnexpectedType
        {
            private bool _value;

            public static int SetterCallCount { get; set; }

            public bool Value
            {
                get => _value;
                set
                {
                    SetterCallCount++;
                    _value = value;
                }
            }
        }
    }
}
