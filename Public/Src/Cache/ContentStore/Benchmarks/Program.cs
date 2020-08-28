using BenchmarkDotNet.Running;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args); return;

            // Used for profiling.
            var benchmark = new ManagedChunkerBenchmarks();
            benchmark.Case = new ManagedChunkerBenchmarks.TestCase(new ManagedChunker(new ChunkerConfiguration(64 * 1024)));
            benchmark.GlobalSetup();

            for (int i = 0; i < 10; i++)
            {
                benchmark.RunChunker();
            }
        }
    }
}
