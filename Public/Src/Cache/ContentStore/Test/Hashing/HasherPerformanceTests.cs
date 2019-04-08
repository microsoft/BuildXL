// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using ContentStoreTest.Performance;
using FluentAssertions;
using BuildXL.Cache.ContentStore.InterfacesTest;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1402 // File may only contain a single class

// ReSharper disable UnusedMember.Global
namespace ContentStoreTest.Performance.Hashing
{
    public abstract class HasherPerformanceTests : TestWithOutput
    {
        private const int Iterations = 10000;
        private readonly PerformanceResultsFixture _resultsFixture;
        private readonly IContentHasher _hasher;
        private bool _disposed;

        protected HasherPerformanceTests(ITestOutputHelper output, HashType hashType, PerformanceResultsFixture resultsFixture)
            : base(output)
        {
            _resultsFixture = resultsFixture;
            _hasher = HashInfoLookup.Find(hashType).CreateContentHasher();
        }

        public override void Dispose()
        {
            base.Dispose();

            if (_disposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);

            _disposed = true;
        }

        private void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _hasher.Dispose();
        }

        [Fact]
        public void Size1K()
        {
            Run(nameof(Size1K), 1024);
        }

        [Fact]
        public void Size16K()
        {
            Run(nameof(Size16K), 1024 * 16, iterations: Iterations / 10);
        }

        [Fact]
        public void Size1M()
        {
            Run(nameof(Size1M), 1024 * 1024, iterations: Iterations / 100);
        }

        private void Run(string method, int contentSize, int iterations = Iterations)
        {
            var content = ThreadSafeRandom.GetBytes(contentSize);

            PerfTestCase(); // warm up

            var stopwatch = Stopwatch.StartNew();

            for (var i = 0; i < iterations; i++)
            {
                PerfTestCase();
            }

            stopwatch.Stop();

            var rate = (long)(iterations / stopwatch.Elapsed.TotalSeconds);
            var name = GetType().Name + "." + method;
            _resultsFixture.AddResults(Output, name, rate, "items/sec", iterations);

            void PerfTestCase()
            {
                var x = _hasher.GetContentHash(content);
                if (x.ByteLength != _hasher.Info.ByteLength)
                {
                    throw new InvalidOperationException("unexpected hash length");
                }
            }
        }

        [Fact]
        public void CreateContentHasher()
        {
            var hashInfo = HashInfoLookup.Find(_hasher.Info.HashType);

            // Warm-up session.
            PerfTestCase();

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < Iterations; i++)
            {
                PerfTestCase();
            }

            stopwatch.Stop();

            var rate = (long)(Iterations / stopwatch.Elapsed.TotalSeconds);
            var name = GetType().Name + "." + nameof(CreateContentHasher);
            _resultsFixture.AddResults(Output, name, rate, "items/sec", Iterations);

            void PerfTestCase()
            {
                using (var hasher = hashInfo.CreateContentHasher())
                {
                    hasher.Info.HashType.Should().Be(_hasher.Info.HashType);
                }
            }
        }

        [Fact]
        public void CreateFromBytes()
        {
            const int count = 2000000;
            var hashType = _hasher.Info.HashType;
            var hashBytes = ThreadSafeRandom.GetBytes(_hasher.Info.ByteLength);

            PerfTest(); // Warm-up invocation.

            var stopwatch = Stopwatch.StartNew();

            for (var i = 0; i < count; i++)
            {
                PerfTest();
            }

            stopwatch.Stop();

            var rate = (long)(count / stopwatch.Elapsed.TotalSeconds);
            var name = GetType().Name + "." + nameof(CreateFromBytes);
            _resultsFixture.AddResults(Output, name, rate, "items/sec", count);

            void PerfTest()
            {
                var hash = new ContentHash(hashType, hashBytes);
                DoSomethingWith(hash);
            }
        }

        private void DoSomethingWith(ContentHash hash)
        {
            hash.HashType.Should().Be(_hasher.Info.HashType);
        }
    }

    [Trait("Category", "Performance")]
    [Collection("Performance")]
    public class SHA1HasherPerformanceTests : HasherPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SHA1HasherPerformanceTests(ITestOutputHelper output, PerformanceResultsFixture resultsFixture)
            : base(output, HashType.SHA1, resultsFixture)
        {
        }
    }

    [Trait("Category", "Performance")]
    [Collection("Performance")]
    public class SHA256HasherPerformanceTests : HasherPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public SHA256HasherPerformanceTests(ITestOutputHelper output, PerformanceResultsFixture resultsFixture)
            : base(output, HashType.SHA256, resultsFixture)
        {
        }
    }

    [Trait("Category", "Performance")]
    [Collection("Performance")]
    public class MD5HasherPerformanceTests : HasherPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public MD5HasherPerformanceTests(ITestOutputHelper output, PerformanceResultsFixture resultsFixture)
            : base(output, HashType.MD5, resultsFixture)
        {
        }
    }

    [Trait("Category", "Performance")]
    [Collection("Performance")]
    public class VsoHasherPerformanceTests : HasherPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public VsoHasherPerformanceTests(ITestOutputHelper output, PerformanceResultsFixture resultsFixture)
            : base(output, HashType.Vso0, resultsFixture)
        {
        }
    }

    [Trait("Category", "Performance")]
    [Collection("Performance")]
    public class DedupChunkHasherPerformanceTests : HasherPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public DedupChunkHasherPerformanceTests(ITestOutputHelper output, PerformanceResultsFixture resultsFixture)
            : base(output, HashType.DedupChunk, resultsFixture)
        {
        }
    }

    [Trait("Category", "Performance")]
    [Trait("Category", "WindowsOSOnly")]
    [Collection("Performance")]
    public class DedupNodeHasherPerformanceTests : HasherPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public DedupNodeHasherPerformanceTests(ITestOutputHelper output, PerformanceResultsFixture resultsFixture)
            : base(output, HashType.DedupNode, resultsFixture)
        {
        }
    }
}
