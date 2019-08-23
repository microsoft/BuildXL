using System;
using System.Diagnostics;
using System.IO;
using ContentPlacementAnalysisTools.Core;
using static ContentPlacementAnalysisTools.ML.Main.DataConsolidator;

namespace ContentPlacementAnalysisTools.ML.Action
{
    /// <summary>
    /// This action converts a csv file to arff format wich is lighter. Its used in weka
    /// </summary>
    public class WriteArff : TimedAction<SampleArtifactsOutput, WriteArffOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private ApplicationConfiguration m_config = null;
        /// <summary>
        /// Constructor
        /// </summary>
        public WriteArff(ApplicationConfiguration config)
        {
            m_config = config;
        }

        /// <summary>
        /// Invoke weka jar to create an arff from a csv file 
        /// </summary>
        protected override WriteArffOutput Perform(SampleArtifactsOutput input)
        {
            s_logger.Debug($"WriteArff starts");
            try
            {
                var outputFile = $"{input.SampleFileName}.arff";
                // lets analyze this in here
                try
                {
                    var exitCode = RunArffWriter(input, outputFile);
                    s_logger.Debug($"WriteArff returns exitCode={exitCode}");
                }
                catch (Exception e)
                {
                    s_logger.Error(e, "An exception ocurred when writting arff");
                }
                finally
                {
                    if (!File.Exists(outputFile))
                    {
                        throw new Exception($"Arff wtite task failed to write output to [{outputFile}]");
                    }
                }
                // done
                return new WriteArffOutput();
            }
            finally
            {
                s_logger.Debug($"WriteArff ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
        }

        private int RunArffWriter(SampleArtifactsOutput input, string outputName)
        {
            var proc = new Process();
            proc.StartInfo.FileName = "java";
            proc.StartInfo.Arguments = $"-cp {Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), m_config.WekaConfig.WekaJar)} " +
                $"{m_config.WekaConfig.WekaCsvToArffCommand.Replace("{0}", input.SampleFileName).Replace("{1}", Convert.ToString(input.NumSamples))}";
            s_logger.Debug($"Command: {proc.StartInfo.FileName} {proc.StartInfo.Arguments}");
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.Start();
            var procReader = proc.StandardOutput;
            var writer = new StreamWriter(outputName);
            string line;
            while ((line = procReader.ReadLine()) != null)
            {
                writer.WriteLine(line);
            }
            writer.Close();
            proc.WaitForExit();
            var exitCode = proc.ExitCode;
            proc.Close();
            return exitCode;
        }

        /// <inheritdoc />
        protected override void CleanUp(SampleArtifactsOutput input, WriteArffOutput output) { }
        /// <inheritdoc />
        protected override void Setup(SampleArtifactsOutput input){}
    }

    /// <summary>
    /// Placeholder
    /// </summary>
    public class WriteArffOutput{}
}
