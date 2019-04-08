// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Tool.Mimic
{
    /// <summary>
    /// Writes a file
    /// </summary>
    internal sealed class WriteFile
    {
        internal readonly string Path;
        internal readonly long LengthInBytes;
        internal readonly string RepeatingContent;

        internal WriteFile(string path, long lengthInBytes, string repeatingContent)
        {
            Path = path;
            LengthInBytes = lengthInBytes;
            RepeatingContent = repeatingContent;
        }

        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        internal void Write(bool createDirectory, bool ignoreFilesOverlappingDirectories, double scaleFactor)
        {
            if (createDirectory)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            }

            if (ignoreFilesOverlappingDirectories && Directory.Exists(Path))
            {
                return;
            }

#pragma warning disable CA5351 // Do not use insecure cryptographic algorithm MD5.
            using (var md5 = MD5.Create())
#pragma warning restore CA5351 // Do not use insecure cryptographic algorithm MD5.
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(RepeatingContent ?? string.Empty));
                int seedFromHash = BitConverter.ToInt32(hash, 0);

                Random rng = new Random(seedFromHash);
                byte[] bytes = new byte[8192];

                long remainingScaledFileSize = (long)(LengthInBytes * scaleFactor);

                using (FileStream s = new FileStream(Path, FileMode.Create, FileAccess.Write))
                {
                    using (BinaryWriter writer = new BinaryWriter(s, Encoding.ASCII, leaveOpen: true))
                    {
                        while (remainingScaledFileSize > 0)
                        {
                            rng.NextBytes(bytes);
                            writer.Write(bytes, 0, (int)Math.Min(bytes.Length, remainingScaledFileSize));
                            remainingScaledFileSize -= bytes.Length;
                        }
                    }
                }
            }

            Console.WriteLine("Write File: {0}", Path);
        }
    }
}
