using System;
using System.Collections.Generic;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Benchmarks
{
    [MemoryDiagnoser, Config(typeof(Config))]
    public class ManagedChunkerBenchmarks
    {
        private const int TotalBytes = 32 * 1024 * 1024; // 32 MB
        private byte[][] _buffers;

        private class Config : ManualConfig
        {
            public Config() => AddColumn(new ThroughputColumn(TotalBytes, error: false), new ThroughputColumn(TotalBytes, error: true));
        }

        public class TestCase
        {
            public readonly IChunker Chunker;
            public TestCase(IChunker chunker) => Chunker = chunker;
            public override string ToString() => $"{Chunker.GetType().Name.Replace("Chunker", "")} - {new SizeValue(Chunker.Configuration.AvgChunkSize).ToString(CultureInfo.InvariantCulture)}";
        }

        [ParamsSource(nameof(TestCases))]
        public TestCase Case { get; set; }

        public static IEnumerable<TestCase> TestCases => new[]
            {
                new TestCase(new ManagedChunker(new ChunkerConfiguration(64 * 1024))),
                new TestCase(new ManagedChunker(new ChunkerConfiguration(4 * 1024 * 1024))),
                new TestCase(new ComChunker(new ChunkerConfiguration(64 * 1024)))
            };

        [GlobalSetup]
        public void GlobalSetup()
        {
            int bufferSize = Case.Chunker.Configuration.AvgChunkSize * 2;
            if (TotalBytes % bufferSize != 0)
            {
                throw new ArgumentException("Total bytes is not a multiple of AvgChunkSize*2.");
            }

            int bufferCount = TotalBytes / bufferSize;

            var random = new Random();
            _buffers = new byte[bufferCount][];
            for (int i = 0; i < bufferCount; i++)
            {
                _buffers[i] = new byte[bufferSize];
                random.NextBytes(_buffers[i]);
            }

            Case.Chunker.BeginChunking(c => { }).Dispose(); // Warm up array pool.
        }

        [Benchmark]
        public uint RunChunker()
        {
            uint totalChunkSize = 0; // To ensure nothing is optimized out.
            using (IChunkerSession session = Case.Chunker.BeginChunking(c => totalChunkSize += c.Size))
            {
                for (int i = 0; i < _buffers.Length; i++)
                {
                    session.PushBuffer(_buffers[i], 0, _buffers[i].Length);
                }

                return totalChunkSize;
            }
        }
    }
}
