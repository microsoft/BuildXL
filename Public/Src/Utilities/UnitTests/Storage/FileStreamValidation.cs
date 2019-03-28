// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.Storage
{
    /// <summary>
    /// Utilities for generating files with known patterns and later validating them.
    /// This enables testing that reads and writes to a file or buffer are returning accurate data.
    /// </summary>
    public static class FileStreamValidation
    {
        /// <summary>
        /// Validates a buffer matches the expected pattern at the given size.
        /// </summary>
        public static void ValidateBuffer(byte[] buffer, int numberOfBytes)
        {
            using (var stream = new MemoryStream(buffer, writable: false))
            {
                ValidateStream(stream, numberOfBytes);
            }
        }

        /// <summary>
        /// Validates a stream matches the expected pattern at the given size.
        /// </summary>
        public static void ValidateStream(Stream stream, int numberOfBytes)
        {
            Contract.Requires(numberOfBytes % 4 == 0, "Byte count must be a multiple of 4");

            using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false))
            {
                int numberOfCompleteWords = numberOfBytes / 4;
                for (int i = 0; i < numberOfCompleteWords; i++)
                {
                    XAssert.AreEqual(i, reader.ReadInt32());
                }

                XAssert.IsTrue(reader.Read() == -1, "Expected end of file after the validation pattern (stream too long)");
            }
        }

        /// <summary>
        /// Writes a file that can later be validated. The <paramref name="numberOfBytes" /> given to the validator must be the same.
        /// </summary>
        public static void WriteValidatableFile(string path, int numberOfBytes)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Delete))
            {
                WriteValidatableContent(fs, numberOfBytes);
            }
        }

        /// <summary>
        /// Creates a buffer that can later be validated. The <paramref name="numberOfBytes" /> given to the validator must be the same.
        /// </summary>
        public static byte[] CreateValidatableBuffer(int numberOfBytes)
        {
            Contract.Requires(numberOfBytes % 4 == 0, "Byte count must be a multiple of 4");

            var buffer = new byte[numberOfBytes];
            using (var stream = new MemoryStream(buffer, writable: true))
            {
                WriteValidatableContent(stream, numberOfBytes);
            }

            return buffer;
        }

        private static void WriteValidatableContent(Stream stream, int numberOfBytes)
        {
            Contract.Requires(numberOfBytes % 4 == 0, "Byte count must be a multiple of 4");

            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                int numberOfCompleteWords = numberOfBytes / 4;
                for (int i = 0; i < numberOfCompleteWords; i++)
                {
                    writer.Write((int)i);
                }
            }
        }
    }
}
