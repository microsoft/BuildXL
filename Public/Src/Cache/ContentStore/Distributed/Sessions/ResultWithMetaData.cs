using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    internal class ResultWithMetaData<T>
    {
        public ResultMetaData Metadata { get; }

        public T Result { get; }

        public ResultWithMetaData(
            ResultMetaData metadata,
            T result)
        {
            Metadata = metadata;
            Result = result;
        }

    }
}
