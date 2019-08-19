using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Net;
using ContentPlacementAnalysisTools.Core;
using ContentPlacementAnalysisTools.Extraction.Main;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This is the action that downloads a single build. It takes as input an object of type 
    /// </summary>
    public class BuildDownload : TimedAction<KustoBuild, BuildDownloadOutput>
    {

        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ApplicationConfiguration m_configuration = null;
        private string m_bxlPath = null;
        private string m_dominoPath = null;
        private string m_outputDirectory = null;

        /// <summary>
        /// Constructor
        /// </summary>
        public BuildDownload(ApplicationConfiguration config, string outputDirectory)
        {
            m_configuration = config;
            m_outputDirectory = outputDirectory;
        }

        /// <inheritdoc />
        protected override void CleanUp(KustoBuild input, BuildDownloadOutput output) {}

        /// <inheritdoc />
        protected override void Setup(KustoBuild input)
        {
            // do some checks in here
            m_bxlPath = Path.Combine(m_outputDirectory, input.BuildControllerMachineName);
            m_dominoPath = Path.Combine(m_outputDirectory, input.BuildControllerMachineName, "Domino");
            // create the directories
            Directory.CreateDirectory(m_bxlPath);
            Directory.CreateDirectory(m_dominoPath);
            Contract.Requires(Directory.Exists(m_bxlPath) && Directory.Exists(m_dominoPath), "Could not create directories for downloads...");
        }

        /// <summary>
        /// Given a machine and log dir, downloads the zip files used to reconstructuc the build log 
        /// </summary>
        protected override BuildDownloadOutput Perform(KustoBuild input)
        {
            s_logger.Debug($"BuildDownloader starts...");
            try
            {
                var bxlZip = Path.Combine(m_bxlPath, "BuildXLLogs.zip");
                var dominoZip = Path.Combine(m_dominoPath, "Domino.zip");
                // build the urls
                var urls = BuildDownloadUrls(input);
                var downloaded = 0;
                // and download the two of them
                foreach (var url in urls)
                {
                    // download each one
                    if (DownloadFileTo(url, bxlZip, dominoZip))
                    {
                        ++downloaded;
                    }
                }
                // now, the output contains the number of downloaded files (that should be two)
                // and the base directory of where they are
                return new BuildDownloadOutput(m_bxlPath, downloaded, bxlZip, dominoZip, input);
            }
            finally
            {
                s_logger.Debug($"BuildDownloader ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
            
        }

        private string[] BuildDownloadUrls(KustoBuild buildData) => new string[] {
            $"https://b/getfile?path=\\\\{buildData.BuildControllerMachineName}\\{buildData.LogDirectory}\\Logs\\ProductBuild\\BuildXLLogs.zip",
            $"https://b/getfile?path=\\\\{buildData.BuildControllerMachineName}\\{buildData.LogDirectory}\\Logs\\ProductBuild\\Domino\\Domino.zip"
        };

        private bool DownloadFileTo(string url, string bxl, string domino)
        {
            var filePath = url.Contains("Domino.zip") ? domino : bxl;
            using (var client = new WebClient())
            {
                try
                {
                    client.UseDefaultCredentials = true;
                    // download here...
                    client.DownloadFile(url, filePath);
                    // and done...
                    return File.Exists(filePath);
                }
                catch (Exception e)
                {
                    s_logger.Error(e, "Could not download file...");
                    throw;
                }
            }
        }

    }

    /// <summary>
    /// Build download output, contains the main path where the files where downloaded and the number of files downloaded
    /// </summary>
    public class BuildDownloadOutput
    {
        /// <summary>
        /// Path where the downloads are located
        /// </summary>
        public string OutputDirectory { get; }
        /// <summary>
        /// Path where the bxl zip file was downloaded
        /// </summary>
        public string BxlZipOutputDirectory { get; }
        /// <summary>
        /// Path where the domino zip file was downloaded
        /// </summary>
        public string DominoZipOutputDirectory { get; }
        /// <summary>
        /// Number of downloaded files
        /// </summary>
        public int DownloadedFiles { get; }
        /// <summary>
        /// The data for the build we are downloading
        /// </summary>
        public KustoBuild BuildData { get; }

        /// <summary>
        /// Base constructor
        /// </summary>
        public BuildDownloadOutput(string outputDir, int numDownloadedFiles, string bxl, string domino, KustoBuild bd)
        {
            OutputDirectory = outputDir;
            DownloadedFiles = numDownloadedFiles;
            BxlZipOutputDirectory = bxl;
            DominoZipOutputDirectory = domino;
            BuildData = bd;
        }
    }

}
