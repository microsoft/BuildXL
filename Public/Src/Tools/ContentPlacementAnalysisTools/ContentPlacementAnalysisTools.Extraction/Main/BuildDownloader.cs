using System;
using System.IO;
using BuildXL.ToolSupport;
using ContentPlacementAnalysisTools.Extraction.Action;

namespace ContentPlacementAnalysisTools.Extraction.Main
{

    /// <summary>
    /// Main class for downloading a single build given a machine name and a log dir
    /// </summary>
    public class BuildDownloader
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private static void Main(string[] args)
        {
            try
            {
                // parse args
                var arguments = new Args(args);
                s_logger.Info($"Downloading build, params=[machine={arguments.Machine}, logDir={arguments.LogDirectory}, outputDir={arguments.OutputDirectory}]");
                var input = new BuildDownloaderInput(arguments.Machine, arguments.LogDirectory, arguments.OutputDirectory);
                var action = new Action.BuildDownloader();
                var output = action.PerformActionWithResult(input);
                s_logger.Info($"ExecutionStatus={output.ExecutionStatus}");
                if (output.ExecutionStatus)
                {
                    s_logger.Info($"DownloadedFiles={output.Result.DownloadedFiles}");
                    s_logger.Info($"OutputDirectory={output.Result.OutputDirectory}");
                }
            }
            finally
            {
                s_logger.Info("Build downloaded...");
            }
        }
    }

    internal sealed partial class Args : CommandLineUtilities
    {
        public readonly string Machine;
        public readonly string LogDirectory;
        public readonly string OutputDirectory;

        public Args(string[] args) : base(args)
        {
            foreach (Option opt in Options)
            {
                if (opt.Name.Equals("machine", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("m", StringComparison.OrdinalIgnoreCase))
                {
                    Machine = ParseStringOption(opt);
                }
                else if(opt.Name.Equals("logDirectory", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("ld", StringComparison.OrdinalIgnoreCase))
                {
                    LogDirectory = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("od", StringComparison.OrdinalIgnoreCase))
                {
                    OutputDirectory = ParseStringOption(opt);
                }
            }
        }
    }
}
