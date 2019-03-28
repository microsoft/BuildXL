// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
