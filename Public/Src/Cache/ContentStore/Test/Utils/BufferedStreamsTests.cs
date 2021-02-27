// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Utils;
using Google.Protobuf;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Utils
{
    public class BufferedStreamsTests
    {
        [Fact]
        public async Task ReadStreamDoesNotOverflowOnMoreThan2GB()
        {
            // This test should never fail with OverflowException
            var repeated = new byte["128 MB".ToSize()];

            Func<Task<ByteString>> producer = async () => await Task.FromResult(ByteString.CopyFrom(repeated));

            using (var readStream = new BufferedReadStream(producer))
            {
                var data = new byte["128 MB".ToSize()];
                while (readStream.Position < "4 GB".ToSize())
                {
                    await readStream.ReadAsync(data, 0, data.Length);
                }
            }
        }

        [Fact]
        public async Task WriteStreamDoesNotOverflowOnMoreThan2GB()
        {
            // This test should never fail with OverflowException
            var storage = new byte[4096];
            using (var writeStream = new BufferedWriteStream(storage, (buffer, start, end) => Task.CompletedTask))
            {
                var data = new byte["1 GB".ToSize()];
                await writeStream.WriteAsync(data, 0, data.Length);
                await writeStream.WriteAsync(data, 0, data.Length);
                await writeStream.WriteAsync(data, 0, data.Length);
            }
        }
    }
}
