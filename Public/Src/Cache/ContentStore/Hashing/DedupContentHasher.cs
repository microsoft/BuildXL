using System;
using System.IO;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Dedup content hasher which is hashtype aware.
    /// </summary>
    public class DedupContentHasher<T> : ContentHasher<T>, IDedupContentHasher where T : DedupNodeOrChunkHashAlgorithm, new()
    {
        /// <nodoc />
        public DedupContentHasher(HashInfo info) : base(info)
        {
        }

        /// <nodoc />
        public async Task<DedupNode> HashContentAndGetDedupNodeAsync(StreamWithLength content)
        {
            var contentHashDedupNodeTuple = await GetContentHashInternalAsync(content); // filestream has length so this is okay?
            if (contentHashDedupNodeTuple.Item2 == null)
            {
                throw new InvalidOperationException($"{nameof(HashContentAndGetDedupNodeAsync)}: dedup node was not retrievable after hashing content - incompatible hash type.");
            }
            return (DedupNode)contentHashDedupNodeTuple.Item2;
        }
    }

    /// <summary>
    /// Copied from ArtifactServices\Shared\Content.Common\FileSystem\FileStreamUtils.cs
    /// TODO: Chunk size optimization - centralize this when fixing BlobIdentifiers.cs
    /// </summary>
    public static class FileStreamUtility
    {
        /// <summary>
        ///  Disable FileStream's buggy buffering
        /// </summary>
        public const int RecommendedFileStreamBufferSize = 1;

        /// <nodoc />
        public static FileStream OpenFileStreamForAsync(
            string filePath,
            FileMode mode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions extraOptions = FileOptions.None)
        {
            try
            {
                return new FileStream(filePath, mode, fileAccess, fileShare,
                    bufferSize: RecommendedFileStreamBufferSize,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan | extraOptions);
            }
            catch (PathTooLongException tooLong)
            {
                throw new PathTooLongException($"Path is too long: '{filePath}'", tooLong);
            }
        }
    }
}
