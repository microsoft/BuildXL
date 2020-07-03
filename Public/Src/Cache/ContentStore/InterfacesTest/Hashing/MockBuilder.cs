// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    internal static class MockBuilder
    {
        public static byte[] GetContent(int contentLength = 1000)
        {
            byte[] content = new byte[contentLength];
            for (int i = 0; i < contentLength; i++)
            {
                content[i] = (byte)(i % byte.MaxValue);
            }

            return content;
        }

        public static Stream GetContentStream(int contentLength = 1000)
        {
            return new MemoryStream(GetContent(contentLength));
        }
    }
}
