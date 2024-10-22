// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BuildToolsInstaller.Utiltiies
{
    /// <nodoc />
    public interface INugetDownloader
    {
        public Task<bool> TryDownloadNugetToDiskAsync(SourceRepository sourceRepository, string package, NuGetVersion version, string downloadLocation, ILogger logger);
    }
}
