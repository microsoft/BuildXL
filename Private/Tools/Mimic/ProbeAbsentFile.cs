// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace Tool.Mimic
{
    /// <summary>
    /// Enumerates a directory
    /// </summary>
    internal sealed class ProbeAbsentFile
    {
        internal readonly string Path;

        internal ProbeAbsentFile(string path)
        {
            Path = path;
        }

        internal void Probe()
        {
            Console.WriteLine("Probe: {0}. Exists: {1}", Path, File.Exists(Path));
            return;
        }
    }
}
