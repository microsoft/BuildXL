using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Test;
using BuildXL.Cache.ContentStore.Vsts;
using FluentAssertions;
using Microsoft.VisualStudio.Services.Content.Common;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Vsts.Test
{
    public class DedupContentSessionTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1024)]
        [InlineData(128*1024+1)] // Chunk size is 128 * 1024
        public async Task CanGetHashFromFile(int fileLength)
        {
            using var stream = new MemoryStream(new byte[fileLength]);
            var node = await DedupContentSession.GetDedupNodeFromFileAsync(string.Empty, new TestFileSystem(stream), CancellationToken.None);
            node.HashString.Should().NotBeNullOrEmpty();
        }

        [MtaFact]
        public void ComChunkerWorksOnThreading()
        {
            Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.MTA);
            var chunker = new ComChunker();
            Task.Run(() =>
            {
                using var session = chunker.BeginChunking(chunk => { });
                session.PushBuffer(new ArraySegment<byte>(new byte[1]));
            }).GetAwaiter().GetResult();
        }

        private class TestFileSystem : IFileSystem
        {
            private readonly Stream _stream;

            public TestFileSystem(Stream stream)
            {
                _stream = stream;
            }
            public void CreateDirectory(string directoryPath) => throw new NotImplementedException();
            public void DeleteFile(string filePath) => throw new NotImplementedException();
            public bool DirectoryExists(string directoryPath) => throw new NotImplementedException();
            public IEnumerable<string> EnumerateDirectories(string directoryFullPath, bool recursiveSearch) => throw new NotImplementedException();
            public IEnumerable<string> EnumerateFiles(string directoryFullPath, bool recursiveSearch) => throw new NotImplementedException();
            public bool FileExists(string filePath) => throw new NotImplementedException();
            public long GetFileSize(string filePath) => throw new NotImplementedException();
            public string GetRandomFileName() => throw new NotImplementedException();
            public string GetTempFullPath() => throw new NotImplementedException();
            public string GetWorkingDirectory() => throw new NotImplementedException();
            public FileStream OpenFileStreamForAsync(string fileFullPath, FileMode fileMode, FileAccess fileAccess, FileShare fileShare) => throw new NotImplementedException();
            public Stream OpenStreamForFile(string fileFullPath, FileMode fileMode, FileAccess fileAccess, FileShare fileShare) => _stream;
            public StreamReader OpenText(string filePath) => throw new NotImplementedException();
            public byte[] ReadAllBytes(string filePath) => throw new NotImplementedException();
            public string ReadAllText(string filePath) => throw new NotImplementedException();
            public void WriteAllBytes(string filePath, byte[] bytes) => throw new NotImplementedException();
            public void WriteAllText(string filePath, string content) => throw new NotImplementedException();
            public TempFile GetTempFileFullPath() => throw new NotImplementedException();
        }
    }
}
