using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    internal enum MemoizationStoreCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetLevelSelectors,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetContentHashList,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        AddOrGetContentHashList,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        IncorporateStrongFingerprints,

        /// <nodoc />
        GetLevelSelectorsRetries,

        /// <nodoc />
        GetContentHashListRetries,

        /// <nodoc />
        AddOrGetContentHashListRetries,

        /// <nodoc />
        IncorporateStrongFingerprintsRetries,

        /// <nodoc />
        GetSelectorsCalls,
    }
}
