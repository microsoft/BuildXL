using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlacementAnalysisTools.Core.Kusto;
using ContentPlacementAnalysisTools.Extraction.Main;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This is the action that downloads a single build. It takes as input an object of type 
    /// </summary>
    public class BuildDownload : TimedAction<List<KustoBuild>, DecompressionOutput>
    {

        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ApplicationConfiguration m_configuration = null;
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
        protected override void CleanUp(List<KustoBuild> input, DecompressionOutput output) {}

        /// <inheritdoc />
        protected override void Setup(List<KustoBuild> input){}

        /// <summary>
        /// Given a machine and log dir, downloads the zip files used to reconstructuc the build log 
        /// </summary>
        protected override DecompressionOutput Perform(List<KustoBuild> inputs)
        {
            s_logger.Debug($"BuildDownloader starts...");
            try
            {
                foreach (var input in inputs)
                {
                    s_logger.Debug($"Trying out build={input.BuildId}");
                    // do some checks in here
                    var bxlPath = Path.Combine(m_outputDirectory, $"{input.BuildId}-{input.BuildControllerMachineName}");
                    var dominoPath = Path.Combine(bxlPath, "Domino");
                    // create the directories
                    Directory.CreateDirectory(bxlPath);
                    Directory.CreateDirectory(dominoPath);
                    if(!Directory.Exists(bxlPath) || !Directory.Exists(dominoPath))
                    {
                        // error here
                        throw new Exception($"Could not create directories for build download (bid={input.BuildId})");
                    }
                    var bxlZip = Path.Combine(bxlPath, "BuildXLLogs.zip");
                    var dominoZip = Path.Combine(dominoPath, "Domino.zip");
                    // build the urls
                    var urls = BuildDownloadUrls(input, m_configuration.UseCBTest);
                    var downloaded = 0;
                    // and download the two of them
                    try
                    {
                        foreach (var url in urls)
                        {
                            // download each one
                            if (DownloadFileTo(url, bxlZip, dominoZip))
                            {
                                ++downloaded;
                            }
                        }
                        // now, the output is the result of the decompression
                        var decompression = new Decompression(m_configuration);
                        var decompressionResult = decompression.PerformAction(new BuildDownloadOutput(bxlPath, downloaded, bxlZip, dominoZip, input));
                        if (decompressionResult.ExecutionStatus)
                        {
                            return decompressionResult.Result;
                        }
                        else
                        {
                            throw decompressionResult.Exception;
                        }
                        
                    }
                    catch(Exception e)
                    {
                        s_logger.Error(e, $"Build download for [{input.BuildId}] failed, retrying with another...");
                        // delete dirs
                        if (Directory.Exists(bxlPath))
                        {
                            Directory.Delete(bxlPath, true);
                        }
                        // continue here
                        continue;
                    }
                }
                // everything failed...
                throw new Exception($"All downloads failed (count={inputs.Count})");

            }
            finally
            {
                s_logger.Debug($"BuildDownloader ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
            
        }

        private string[] BuildDownloadUrls(KustoBuild buildData, bool isCBTest) => 
            isCBTest? 
                new string[] {
                    $"https://cbtest/getfile?path=\\\\{buildData.BuildControllerMachineName}\\{buildData.LogDirectory}\\Logs\\ProductBuild\\BuildXLLogs.zip",
                    $"https://cbtest/getfile?path=\\\\{buildData.BuildControllerMachineName}\\{buildData.LogDirectory}\\Logs\\ProductBuild\\Domino\\Domino.zip"
                 }
                :
                new string[] {
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
