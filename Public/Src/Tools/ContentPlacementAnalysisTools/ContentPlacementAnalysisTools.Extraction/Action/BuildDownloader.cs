using System;
using System.IO;
using System.Net;
using ContentPlacementAnalysisTools.Core;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This is the action that downloads a single build. It takes as input an object of type 
    /// </summary>
    public class BuildDownloader : TimedAction<BuildDownloaderInput, BuildDownloaderOutput>
    {

        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        /// <inheritdoc />
        protected override void CleanUp()
        {
            // clean up whatever we need to clean up here
        }

        /// <inheritdoc />
        protected override void Setup()
        {
            // set up in here...
        }

        /// <summary>
        /// Given a machine and log dir, downloads the zip files used to reconstructuc the build log 
        /// </summary>
        protected override BuildDownloaderOutput Perform(BuildDownloaderInput input)
        {
            s_logger.Debug($"BuildDownloader starts...");
            try
            {
                // build the urls
                var urls = input.BuildDownloadUrls();
                var downloaded = 0;
                // and download the two of them
                foreach (var url in urls)
                {
                    // download each one
                    if (DownloadFileTo(url, input.OutputDirectory))
                    {
                        ++downloaded;
                    }
                }
                // now, the output contains the number of downloaded files (that should be two)
                // and the base directory of where they are
                return new BuildDownloaderOutput($"{Path.Combine(input.OutputDirectory, input.MachineName)}", downloaded);
            }
            finally
            {
                s_logger.Debug($"BuildDownloader ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
            
        }


        private bool DownloadFileTo(string url, string baseDirectory)
        {
            string filePath = url.Contains("Domino.zip") ? $"{baseDirectory}\\Domino\\Domino.zip" : $"{baseDirectory}\\BuildXLLogs.zip";
            using (var client = new WebClient())
            {
                try
                {
                    // client.UseDefaultCredentials = true;
                    // download here...
                    client.DownloadFile(url, filePath);
                    // and done...
                    return File.Exists(filePath);

                }
                catch (Exception e)
                {
                    s_logger.Error(e, "Could not download file...");
                    return false;
                }
            }
        }

    }

    /// <summary>
    /// The input for BuildDownloader needs to contain a log dir and a machine name. We need to build a download URL
    /// </summary>
    public class BuildDownloaderInput
    {
        /// <summary>
        /// Location of the files to be downloaded
        /// </summary>
        public string OutputDirectory { get; }
        /// <summary>
        /// Name of the machine where the files will be pulled from. Its the master machine for that build
        /// </summary>
        public string MachineName {get; }
        /// <summary>
        /// Location of the log files for a product build (as obtained querying Kusto)
        /// </summary>
        public string LogDirectory { get; }

        /// <summary>
        /// Base constructor
        /// </summary>
        public BuildDownloaderInput(string machine, string logdir, string outputDir)
        {
            MachineName = machine.Contains(":") ? machine.Split(':')[0] : machine;
            LogDirectory = logdir;
            OutputDirectory = outputDir;
        }

        /// <summary>
        /// Build download urls based on the machine name and product build log. The urls look like:
        /// https://b/getfile?path=\\\\{MachineName}\\{LogDirectory}\\Logs\\ProductBuild\\BuildXLLogs.zip
        /// https://b/getfile?path=\\\\{MachineName}\\{LogDirectory}\\Logs\\ProductBuild\\Domino\\Domino.zip
        /// </summary>
        public string[] BuildDownloadUrls() => new string[] {
            $"https://b/getfile?path=\\\\{MachineName}\\{LogDirectory}\\Logs\\ProductBuild\\BuildXLLogs.zip",
            $"https://b/getfile?path=\\\\{MachineName}\\{LogDirectory}\\Logs\\ProductBuild\\Domino\\Domino.zip"
        };

    }

    /// <summary>
    /// Build download output, contains the main path where the files where downloaded and the number of files downloaded
    /// </summary>
    public class BuildDownloaderOutput
    {
        /// <summary>
        /// Path where the downloads are located
        /// </summary>
        public string OutputDirectory { get; }
        /// <summary>
        /// Number of downloaded files
        /// </summary>
        public int DownloadedFiles { get; }

        /// <summary>
        /// Base constructor
        /// </summary>
        public BuildDownloaderOutput(string outputDir, int numDownloadedFiles)
        {
            OutputDirectory = outputDir;
            DownloadedFiles = numDownloadedFiles;
        }
    }

}
