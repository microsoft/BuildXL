// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Utils
{
    public class BufferedWriteStreamTests
    {
        [Fact]
        public async Task DoesNotOverflowOnMoreThan2GB()
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
