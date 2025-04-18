﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildToolsInstaller.Utilities;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BuildToolsInstaller.Tests
{
    internal class MockNugetDownloader : INugetDownloader
    {
        public (string Repository, string Package, string Version, string DownloadLocation)[] Downloads => m_downloads.ToArray();
        private readonly BlockingCollection<(string Repository, string Package, string Version, string DownloadLocation)> m_downloads = new ();

        public Task<bool> TryDownloadNugetToDiskAsync(SourceRepository sourceRepository, string package, NuGetVersion version, string downloadLocation, ILogger logger)
        {
            m_downloads.Add((sourceRepository.PackageSource.Source, package, version.OriginalVersion!, downloadLocation));
            return Task.FromResult(true);
        }
    }
}
