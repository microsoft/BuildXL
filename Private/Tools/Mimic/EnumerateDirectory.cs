// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;

namespace Tool.Mimic
{
    /// <summary>
    /// Enumerates a directory
    /// </summary>
    internal sealed class EnumerateDirectory
    {
        internal readonly string Path;

        internal EnumerateDirectory(string path)
        {
            Path = path;
        }

        internal void Enumerate()
        {
            string members = "N/A";
            if (Directory.Exists(Path))
            {
                members = string.Join(",", Directory.GetFiles(Path, "*", SearchOption.TopDirectoryOnly));
            }

            Console.WriteLine("Enumerate: {0}. Members: {1}", Path, members);
            return;
        }
    }
}
