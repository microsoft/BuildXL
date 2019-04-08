// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Helper functions related to reading from and writing to streams.
    /// </summary>
    public static class StreamUtilities
    {
        /// <summary>
        /// Reads an array of 32-bit integers
        /// </summary>
        public static int[] ReadInt32Array(this BinaryReader reader, ref byte[] buffer, int count)
        {
            Contract.Requires(reader != null);
            Contract.Requires(count >= 0);
            Array.Resize(ref buffer, count * sizeof(int));

            int offset = 0;
            int left = count * sizeof(int);
            while (left > 0)
            {
                int readCount = reader.Read(buffer, offset, left);
                if (readCount == 0)
                {
                    throw new IOException("Unexpected end of stream");
                }

                offset += readCount;
                left -= readCount;
            }

            var int32Array = new int[count];
            Buffer.BlockCopy(buffer, 0, int32Array, 0, count * sizeof(int));
            return int32Array;
        }
    }
}
