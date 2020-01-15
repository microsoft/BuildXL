// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using ContentStoreTest.Test;
using Xunit;

namespace ContentStoreTest.FileSystem
{
    public class FileSystemHelperTests : TestBase
    {
        public FileSystemHelperTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public Task CatchesIOException()
        {
            return FileSystemHelpers.RetryOnIOException(
                1,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(10),
                exception => { },
                false,
                () => { throw new IOException(); });
        }

        [Fact]
        public Task CatchesIOExceptionMultipleTimes()
        {
            return FileSystemHelpers.RetryOnIOException(
                3,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(10),
                exception => { },
                false,
                () => { throw new IOException(); });
        }

        [Fact]
        public async Task CatchesIOExceptionExceptLast()
        {
            int counter = 0;

            IOException caughtIoException = null;
            try
            {
                await FileSystemHelpers.RetryOnIOException(
                    2,
                    TimeSpan.FromMilliseconds(10),
                    TimeSpan.FromMilliseconds(10),
                    exception => { },
                    true,
                    () =>
                    {
                        counter++;
                        throw new IOException();
                    });
            }
            catch (IOException ioException)
            {
                caughtIoException = ioException;
            }

            Assert.NotNull(caughtIoException);
            Assert.Equal(2, counter);
        }
    }
}
