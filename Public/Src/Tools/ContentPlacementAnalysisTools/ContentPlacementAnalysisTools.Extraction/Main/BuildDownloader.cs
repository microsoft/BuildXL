using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks.Dataflow;
using BuildXL.ToolSupport;
using ContentPlacementAnalysisTools.Core;
using ContentPlacementAnalysisTools.Extraction.Action;
using Kusto.Data;

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
                s_logger.Info($"Downloading build, params=[bid={arguments.BuildId}, outputDir={arguments.OutputDirectory}]");
                // so in here we will create a new network, a simple one, with simple parallelism degree...
                var buildInfoBlock = new TransformBlock<GetKustoBuildInput, TimedActionResult<GetKustoBuildOutput>>(i =>
                {
                    // just execute here
                    var action = new GetKustoBuild();
                    return action.PerformActionWithResult(i);
                });
                var downloadBlock = new TransformBlock<TimedActionResult<GetKustoBuildOutput>, TimedActionResult<BuildDownloadOutput>>( i => 
                {
                    // check
                    if (i.ExecutionStatus)
                    {
                        // just execute here
                        var action = new BuildDownload();
                        var newInput = new BuildDownloadInput(i.Result.KustoBuildData.BuildControllerMachineName, i.Result.KustoBuildData.LogDirectory, arguments.OutputDirectory, i.Result.KustoBuildData);
                        return action.PerformActionWithResult(newInput);
                    }
                    return new TimedActionResult<BuildDownloadOutput>(i.Exception);
                });
                var decompressBlock = new TransformBlock<TimedActionResult<BuildDownloadOutput>, TimedActionResult<DecompressionOutput>>( i =>
                {
                    // check
                    if (i.ExecutionStatus)
                    {
                        // just execute here
                        var action = new Decompression();
                        return action.PerformActionWithResult(i.Result);
                    }
                    return new TimedActionResult<DecompressionOutput>(i.Exception);
                });
                var analysisBlock = new ActionBlock<TimedActionResult<DecompressionOutput>>(i =>
                {
                    // check
                    if (i.ExecutionStatus)
                    {
                        // just execute here
                        var action = new BuildAnalisys();
                        var newInput = new BuildAnalisysInput(arguments.BxlAnalyzerConfigurationFile, i.Result);
                        action.PerformActionWithResult(newInput);
                    }
                });
                // link them
                buildInfoBlock.LinkTo(downloadBlock, new DataflowLinkOptions { PropagateCompletion = true });
                downloadBlock.LinkTo(decompressBlock, new DataflowLinkOptions { PropagateCompletion = true });
                decompressBlock.LinkTo(analysisBlock, new DataflowLinkOptions { PropagateCompletion = true });
                var input = new GetKustoBuildInput(arguments.BuildId, arguments.KustoConnectionConfigurationFile);
                // post the task...
                buildInfoBlock.Post(input);
                // and complete
                buildInfoBlock.Complete();
                // wait for the last...
                analysisBlock.Completion.Wait();
            }
            finally
            {
                s_logger.Info("Done...");
            }
        }
    }

    internal sealed partial class Args : CommandLineUtilities
    {
        public readonly string KustoConnectionConfigurationFile;
        public readonly string BxlAnalyzerConfigurationFile;
        public readonly string OutputDirectory;
        public readonly string BuildId;


        public Args(string[] args) : base(args)
        {
            foreach (var opt in Options)
            {
                if (opt.Name.Equals("kustoConnectionConfiguration", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("kc", StringComparison.OrdinalIgnoreCase))
                {
                    KustoConnectionConfigurationFile = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("od", StringComparison.OrdinalIgnoreCase))
                {
                    OutputDirectory = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("buildId", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("bid", StringComparison.OrdinalIgnoreCase))
                {
                    BuildId = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("bxlAnalyzerConfig", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("bxc", StringComparison.OrdinalIgnoreCase))
                {
                    BxlAnalyzerConfigurationFile = ParseStringOption(opt);
                }
            }
            // and a couple of checks here
            Contract.Requires(BuildId != default && BuildId.Length > 0, "You must specify a build id");
            Contract.Requires(File.Exists(KustoConnectionConfigurationFile), "kustoConnectionConfiguration file does not exists");
            Contract.Requires(File.Exists(BxlAnalyzerConfigurationFile), "bxlAnalyzerConfig file does not exists");
            Contract.Requires(Directory.Exists(OutputDirectory), "Output directory does not exists");
        }
    }
}
