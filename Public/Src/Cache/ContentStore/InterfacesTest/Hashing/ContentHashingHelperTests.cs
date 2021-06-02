// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class ContentHashingHelperTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(VsoHash.BlockSize - 1)]
        [InlineData(VsoHash.BlockSize)]
        [InlineData(VsoHash.BlockSize + 1)]
        [InlineData(2 * VsoHash.BlockSize - 1)]
        [InlineData(2 * VsoHash.BlockSize)]
        [InlineData(2 * VsoHash.BlockSize + 1)]
        public async Task HashBytesOfVariousSizes(int size)
        {
            using var fileSystem = new PassThroughFileSystem();
            using var tempDirectory = new DisposableDirectory(fileSystem);

            var content = ThreadSafeRandom.GetBytes(size);
            var path = tempDirectory.Path / "1.txt";
            fileSystem.WriteAllBytes(path, content);

            var hashType = HashType.Vso0;
            var contentHasher = HashInfoLookup.GetContentHasher(hashType);

            var h1 = contentHasher.GetContentHash(content);
            var h2 = CalculateHashWithMemoryMappedFile(fileSystem, path, hashType);
            Assert.Equal(h1, h2);

#if NET_COREAPP
            h2 = contentHasher.GetContentHash(content.AsSpan());
            Assert.Equal(h1, h2);
#endif

            using var memoryStream = new MemoryStream(content);
            var h3 = await contentHasher.GetContentHashAsync(memoryStream);
            Assert.Equal(h1, h3);

            // Using an old style hashing to make sure it works the same as the new and optimized version.
            using var fileStream = fileSystem.OpenForHashing(path);
            var h4 = await contentHasher.GetContentHashAsync(fileStream);
            Assert.Equal(h1, h4);
        }

        private ContentHash CalculateHashWithMemoryMappedFile(IAbsFileSystem fileSystem, AbsolutePath path, HashType hashType)
        {
#if NET_COREAPP
            using var file = fileSystem.OpenForHashing(path);
            
            return file.ToFileStream().HashFile(hashType);

#else
            return fileSystem.CalculateHashAsync(path, hashType).GetAwaiter().GetResult();
#endif
        }
    }
}
