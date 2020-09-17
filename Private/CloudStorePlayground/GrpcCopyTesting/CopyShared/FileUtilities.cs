using System;
using System.IO;

namespace Helloworld
{
    public static class FileUtilities
    {
        public static Stream OpenFileForReading(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyConstants.BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);
        }

        public static Stream OpenFileForWriting(string path)
        {
            return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, CopyConstants.BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);
        }

    }
}
