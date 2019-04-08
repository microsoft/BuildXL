// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Utils
{
    public class ByteCountUtilitiesTests : TestBase
    {
        public ByteCountUtilitiesTests()
            : base(TestGlobal.Logger)
        {
        }

        private static readonly KeyValuePair<string, long>[] ExpressionToCountMappings =
        {
            new KeyValuePair<string, long>(null, 0),
            new KeyValuePair<string, long>("0", 0),
            new KeyValuePair<string, long>("0bytes", 0),
            new KeyValuePair<string, long>("0B", 0),
            new KeyValuePair<string, long>("0KB", 0),
            new KeyValuePair<string, long>("0MB", 0),
            new KeyValuePair<string, long>("4096", 4096),
            new KeyValuePair<string, long>("4096B", 4096),
            new KeyValuePair<string, long>("4096b", 4096),
            new KeyValuePair<string, long>("4KB", 4096),
            new KeyValuePair<string, long>("1MB", 1024 * 1024),
            new KeyValuePair<string, long>("235MB", 235 * 1024 * 1024),
            new KeyValuePair<string, long>("1GB", 1024 * 1024 * 1024),
            new KeyValuePair<string, long>("24 GB", (long)24 * 1024 * 1024 * 1024),
            new KeyValuePair<string, long>("1TB", (long)1024 * 1024 * 1024 * 1024),
            new KeyValuePair<string, long>("1PB", (long)1024 * 1024 * 1024 * 1024 * 1024),
            new KeyValuePair<string, long>("1EB", (long)1024 * 1024 * 1024 * 1024 * 1024 * 1024)
        };

        [Fact]
        public void ExpressionToCountBasics()
        {
            foreach (var mapping in ExpressionToCountMappings)
            {
                mapping.Key.ToSize().Should().Be(mapping.Value);
            }
        }

        private static readonly KeyValuePair<long, string>[] CountToExpressionMappings =
        {
            new KeyValuePair<long, string>(0, "0 bytes"),
            new KeyValuePair<long, string>(512, "512 bytes"),
            new KeyValuePair<long, string>(1024, "1 KB"),
            new KeyValuePair<long, string>(1024 * 1024, "1 MB"),
            new KeyValuePair<long, string>(1024 * 1024 * 1024, "1 GB"),
            new KeyValuePair<long, string>((long)1024 * 1024 * 1024 * 1024, "1 TB"),
            new KeyValuePair<long, string>((long)1024 * 1024 * 1024 * 1024 * 1024, "1 PB"),
            new KeyValuePair<long, string>((long)1024 * 1024 * 1024 * 1024 * 1024 * 1024, "1 EB"),
            new KeyValuePair<long, string>(235244356, "224.35 MB")
        };

        [Fact]
        public void CountToExpressionBasics()
        {
            foreach (var mapping in CountToExpressionMappings)
            {
                mapping.Key.ToSizeExpression().Should().Be(mapping.Value);
                mapping.Key.ToSizeExpression(true)
                    .Should()
                    .Be(mapping.Key.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
            }
        }

        [Fact]
        public void SuffixWithoutNumber()
        {
            Action byteAction = () => "GB".ToSize();
            byteAction.Should().Throw<ArgumentException>();
        }
    }
}
