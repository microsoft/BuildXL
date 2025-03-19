// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildToolsInstaller.Utilities;
using NuGet.Protocol.Core.Types;

namespace BuildToolsInstaller.Config.Downloader
{
    internal abstract class ConfigurationDownloaderBase
    {
        protected const string ConfigurationPackageName = ConfigurationUtilities.ConfigurationPackageName;
        protected readonly IAdoService AdoService;
        protected readonly ILogger Logger;
        protected readonly INugetDownloader Downloader;
        protected readonly string DownloadPath;
        protected readonly SourceRepository Feed;


        public ConfigurationDownloaderBase(string? feedOverride, IAdoService adoService, ILogger logger)
        {
            AdoService = adoService;
            Logger = logger;
            Downloader = new NugetDownloader();
            DownloadPath = Path.Combine(Path.GetTempPath(), "downloaded_config");
            Feed = NugetHelper.CreateSourceRepository(feedOverride ?? NugetHelper.InferSourceRepository(AdoService));
        }
    }
}
