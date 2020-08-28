using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Benchmarks
{
    public class SingleThreadSHA512Benchmarks : SHA512Benchmarks
    {
        protected override int Parallelism => 1;
    }

    public class AllThreadsSHA512Benchmarks : SHA512Benchmarks
    {
        protected override int Parallelism => Environment.ProcessorCount;
    }

    [MemoryDiagnoser, Config(typeof(Config))]
    public abstract class SHA512Benchmarks
    {
        protected abstract int Parallelism { get; }

        private const int TotalBytes = 4 * 1024 * 1024; // 4 MB
        private static readonly byte[] _buffer = new byte[TotalBytes];

        private class Config : ManualConfig
        {
            public Config() => AddColumn(new ThroughputColumn(TotalBytes, error: false), new ThroughputColumn(TotalBytes, error: true));
        }

        static SHA512Benchmarks() => new Random().NextBytes(_buffer);

        private void RunHash(Func<SHA512> sha)
        {
            Parallel.ForEach(Enumerable.Range(0, Parallelism), _ => {
                using (SHA512 shaLocal = sha())
                {
                    shaLocal.ComputeHash(_buffer).Take(32).ToArray();
                }
            });
        }

        [Benchmark]
        public void Sha512Cng() => RunHash(() => new SHA512Cng());

        [Benchmark]
        public void SHA512CryptoServiceProvider() => RunHash(() => new SHA512CryptoServiceProvider());

        [Benchmark]
        public void SHA512Managed() => RunHash(() => new SHA512Managed());

        [Benchmark]
        public void SHA512OpenSSL()
        {
            Parallel.ForEach(Enumerable.Range(0, Parallelism), _ =>
            {
                using (var sha = new OpenSSL_SHA512())
                {
                    sha.Initialize();
                    sha.HashBuffer(_buffer, 0, _buffer.Length);
                    sha.Finalize(32);
                }
            });
        }
    }
}
