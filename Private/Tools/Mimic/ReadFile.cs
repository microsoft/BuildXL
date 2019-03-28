// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;

namespace Tool.Mimic
{
    /// <summary>
    /// Reades a file
    /// </summary>
    internal sealed class ReadFile
    {
        internal readonly string Path;

        internal ReadFile(string path)
        {
            Path = path;
        }

        internal void Read(bool ignoreIfMissing, bool ignoreFilesOverlappingDirectories)
        {
            if (ignoreIfMissing && !File.Exists(Path))
            {
                return;
            }

            if (ignoreFilesOverlappingDirectories && Directory.Exists(Path))
            {
                return;
            }

            using (StreamReader reader = new StreamReader(Path))
            {
                while (!reader.EndOfStream)
                {
                    reader.ReadLine();
                }
            }

            Console.WriteLine("Read File: {0}.", Path);
        }
    }
}
