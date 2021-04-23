// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
            var (deserialized, type) = DynamicJson.Deserialize(serialized);

            type.Should().Be(typeof(TheClass));
            ((TheClass)deserialized).Inner.Foo.Should().Be(((TheClass)obj).Inner.Foo);
            ((TheClass)deserialized).Inner.Bar.Should().Be(((TheClass)obj).Inner.Bar);
            ((TheClass)deserialized).Value.Should().Be(((TheClass)obj).Value);
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
    }
}
