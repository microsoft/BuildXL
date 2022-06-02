using System;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public static class Utilities
    {
        /// <nodoc />
        public delegate T DeserializeFromSpan<out T>(SpanReader source);

        /// <summary>
        /// Checks that the serialization and deserialization using <see cref="BuildXLReader"/>, <see cref="BuildXLWriter"/> is correct.
        /// As well as the deserialization from <see cref="ReadOnlySpan{T}"/>.
        /// </summary>
        public static void TestSerializationRoundtrip<T>(T expected, Action<BuildXLWriter> serializer, Func<BuildXLReader, T> deserializer, DeserializeFromSpan<T> spanDeserializer)
        {
            using (var ms = new MemoryStream())
            {
                using (var bw = new BuildXLWriter(false, ms, false, false))
                {
                    serializer(bw);
                    ms.Position = 0;

                    using (var reader = new BuildXLReader(false, ms, false))
                    {
                        var deserialized = deserializer(reader);
                        Assert.Equal(expected, deserialized);
                    }

                    SpanReader data = ms.ToArray().AsSpan().AsReader();
                    var deserializedFromSpan = spanDeserializer(data);
                    Assert.Equal(expected, deserializedFromSpan);
                }
            }
        }
    }
}
