using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ContentPlacementAnalysisTools.Core;
using static ContentPlacementAnalysisTools.ML.Main.DataConsolidator;

namespace ContentPlacementAnalysisTools.ML.Action
{
    /// <summary>
    /// This action converts a csv file to arff format wich is lighter. Its used in weka
    /// </summary>
    public class RandomForestFromWeka : TimedAction<RandomForestFromWekaInput, RandomForestFromWekaOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private ApplicationConfiguration m_config = null;
        /// <summary>
        /// Constructor
        /// </summary>
        public RandomForestFromWeka(ApplicationConfiguration config)
        {
            m_config = config;
        }

        /// <summary>
        /// Invoke weka jar to create an arff from a csv file 
        /// </summary>
        protected override RandomForestFromWekaOutput Perform(RandomForestFromWekaInput input)
        {
            s_logger.Debug($"RandomForestFromWeka starts");
            try
            {
                var outputFile = $"{input.TrainingSetCsv}.wtree";
                var outputArffFile = $"{input.TrainingSetCsv}.arff";
                // lets analyze this in here
                try
                {
                    var arffExitCode = WriteArff(input, outputArffFile);
                    s_logger.Debug($"ArffWrite returns exitCode={arffExitCode}");
                    if (File.Exists(outputArffFile))
                    {
                        var exitCode = RunWeka(input, outputArffFile, outputFile);
                        s_logger.Debug($"RandomForestFromWeka returns exitCode={exitCode}");
                    }
                    else
                    {
                        s_logger.Error($"Could not write arff file for [{input.TrainingSetCsv}]");
                    }
                }
                catch (Exception e)
                {
                    s_logger.Error(e, "An exception ocurred when writting forest from weka");
                }
                finally
                {
                    if (!File.Exists(outputFile))
                    {
                        throw new Exception($"RandomForestFromWeka task failed to write output to [{outputFile}]");
                    }
                }
                // and now load the forest
                var forest = RandomForest.FromWekaFile(outputFile, input.Classes);
                // done
                return new RandomForestFromWekaOutput(forest, input.TrainingSetCsv, outputFile);
            }
            finally
            {
                s_logger.Debug($"RandomForestFromWeka ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
        }

        private int RunWeka(RandomForestFromWekaInput input, string arffFile, string outputName)
        {
            var proc = new Process();
            proc.StartInfo.FileName = $"java";
            proc.StartInfo.Arguments = BuildWekaArguments(input, arffFile);
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

        private int WriteArff(RandomForestFromWekaInput input, string outputName)
        {
            var proc = new Process();
            proc.StartInfo.FileName = $"java";
            proc.StartInfo.Arguments = BuildArffArguments(input);
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

        private string BuildArffArguments(RandomForestFromWekaInput input)
        {
            return new StringBuilder()
                .Append($"-cp {Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), m_config.WekaConfig.WekaJar)} ")
                .Append(m_config.WekaConfig.WekaRunArffCommand.Replace("{0}", input.TrainingSetCsv))
                .ToString();
        }

        private string BuildWekaArguments(RandomForestFromWekaInput input, string arffFile)
        {
            return new StringBuilder()
                .Append($"-cp {Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), m_config.WekaConfig.WekaJar)} ")
                .Append($"-Xmx{m_config.WekaConfig.MemoryGB}G -Xms{m_config.WekaConfig.MemoryGB}G ")
                .Append(
                    m_config.WekaConfig.WekaRunRTCommand.Replace("{1}", arffFile)
                    .Replace("{0}", m_config.WekaConfig.RandomTreeConfig.RemovedColumns)
                    .Replace("{2}", Convert.ToString(m_config.WekaConfig.RandomTreeConfig.BagSizePercentage))
                    .Replace("{3}", Convert.ToString(m_config.WekaConfig.RandomTreeConfig.RandomTreeCount))
                    .Replace("{4}", Convert.ToString(m_config.WekaConfig.RandomTreeConfig.MaxCreationParallelism))
                )
                .ToString();
        }

        /// <inheritdoc />
        protected override void CleanUp(RandomForestFromWekaInput input, RandomForestFromWekaOutput output) { }
        /// <inheritdoc />
        protected override void Setup(RandomForestFromWekaInput input){}
    }

    /// <summary>
    /// The input for this type of action
    /// </summary>
    public class RandomForestFromWekaInput
    {
        public HashSet<string> Classes { get; set; }
        public string TrainingSetCsv { get; set; }
        public RandomForestFromWekaInput(string ts, HashSet<string> cl)
        {
            TrainingSetCsv = ts;
            Classes = cl;
        }
    }
    /// <summary>
    /// Conatins the parsed random forest
    /// </summary>
    public class RandomForestFromWekaOutput
    {
        public string PredictorInputFileName { get; set; }
        public string PredictorFileName { get; set; }
        public RandomForest Predictor { get; set; }
        public RandomForestFromWekaOutput(RandomForest p, string ifn, string pfn)
        {
            Predictor = p;
            PredictorInputFileName = ifn;
            PredictorFileName = pfn;
        }
    }
}
