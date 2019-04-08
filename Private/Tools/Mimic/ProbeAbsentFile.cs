// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
