using System;
using System.Diagnostics;
using System.IO;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlacementAnalysisTools.Extraction.Main;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This action runs a bxl analyzer process (content placement) in an already/downloaded and decompressed directory 
    /// </summary>
    public class BuildAnalisys : TimedAction<DecompressionOutput, BuildAnalisysOutput>
    {

        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ApplicationConfiguration m_configuration = null;

        /// <summary>
        /// Constructor
        /// </summary>
        public BuildAnalisys(ApplicationConfiguration config)
        {
            m_configuration = config;
        }

        /// <summary>
        /// After the task is done, the build directory is deleted
        /// </summary>
        protected override void CleanUp(DecompressionOutput input, BuildAnalisysOutput output)
        {
            // finally, just delete the whole target directory
            Directory.Delete(input.OutputDirectory, true);
        }

        /// <summary>
        /// Run an analyzer over a downloaded, decompressed build
        /// </summary>
        protected override BuildAnalisysOutput Perform(DecompressionOutput input)
        {
            s_logger.Debug($"BuildAnalisys starts...");
            try
            {
                var analyzerOutputFile = $"{input.OutputDirectory}\\{constants.JsonResultsFileName}";
                // lets analyze this in here
                try
                {
                    var exitCode = RunBxlAnalyzer(input);
                    s_logger.Debug($"Analyzer returns exitCode={exitCode}");
                }
                catch(Exception e)
                {
                    s_logger.Error(e, "An exception ocurred when running analyzer");
                }
                finally
                {
                    if (!File.Exists(analyzerOutputFile))
                    {
                        throw new Exception($"Analysis task failed to write output to [{analyzerOutputFile}]");
                    }
                }
                // output to a results dir
                var newOutputDirectory = Path.Combine(Directory.GetParent(input.OutputDirectory).FullName, constants.ResultDirectoryName);
                var newOutputPath = Path.Combine(newOutputDirectory, $"{input.BuildData.BuildId}.json");
                // first, move the output file. If this directory already exists it does not matter
                Directory.CreateDirectory(newOutputDirectory);
                File.Move(analyzerOutputFile, newOutputPath);
                // if everything went file, the is a json file waiting for us here...
                return new BuildAnalisysOutput(newOutputPath);
            }
            finally
            {
                s_logger.Debug($"BuildAnalisys ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
           
        }

        /// <inheritdoc />
        protected override void Setup(DecompressionOutput input){}

        private int RunBxlAnalyzer(DecompressionOutput input)
        {
            var proc = new Process();
            proc.StartInfo.FileName = m_configuration.AnalyzerConfig.Exe;
            proc.StartInfo.Arguments = $"/mode:ContentPlacement " +
                $"/sampleProportion:{m_configuration.AnalyzerConfig.SampleProportion} " +
                $"/sampleCountHardLimit:{m_configuration.AnalyzerConfig.SampleCountHardLimit} " +
                $"/executionLog:{input.OutputDirectory}\\Domino.xlg " +
                $"/buildQueue:{input.BuildData.BuildQueue} " +
                $"/buildId:{input.BuildData.BuildId} " +
                $"/buildStartTicks:{input.BuildData.StartTime} " +
                $"/buildDurationMs:{input.BuildData.BuildDurationMs} ";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.Start();
            var procReader = proc.StandardOutput;
            string line;
            while ((line = procReader.ReadLine()) != null)
            {
                s_logger.Debug($"Process logs: {line}");
            }
            proc.WaitForExit();
            var exitCode = proc.ExitCode;
            proc.Close();
            return exitCode;
        }
    }

    /// <summary>
    /// Represents the output of an analysis task, containing the name of the file with results
    /// </summary>
    public class BuildAnalisysOutput
    {
        /// <summary>
        /// The file that contains analysis results
        /// </summary>
        public string AnalyzerOutputFile { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        public BuildAnalisysOutput(string file)
        {
            AnalyzerOutputFile = file;
        }
    }
}
