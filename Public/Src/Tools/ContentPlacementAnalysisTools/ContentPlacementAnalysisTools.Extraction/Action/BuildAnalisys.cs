using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContentPlacementAnalysisTools.Core;
using ContentPlamentAnalysisTools.Core;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This action runs a bxl analyzer process (content placement) in an already/downloaded and decompressed directory 
    /// </summary>
    public class BuildAnalisys : TimedAction<BuildAnalisysInput, BuildAnalisysOutput>
    {

        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private ContentPlacementAnalyzerConfig m_config = null;

        protected override void CleanUp()
        {
            // clean here?
        }

        protected override BuildAnalisysOutput Perform(BuildAnalisysInput input)
        {
            s_logger.Debug($"BuildAnalisys starts...");
            try
            {
                // lets analyze this in here
                RunBxlAnalyzer(input);
                // if everything went file, the is a json file waiting for us here...
                return new BuildAnalisysOutput();
            }
            finally
            {
                s_logger.Debug($"BuildAnalisys ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
           
        }

        protected override void Setup(BuildAnalisysInput input)
        {
            // parse the config here...
            m_config = ContentPlacementAnalyzerConfig.FromJson(input.BxlAnalyzerConfigFile);
        }

        private void RunBxlAnalyzer(BuildAnalisysInput input)
        {
            var proc = new Process();
            proc.StartInfo.FileName = m_config.Exe;
            proc.StartInfo.Arguments = $"/mode:ContentPlacement " +
                $"/sampleProportion:{m_config.SampleProportion} " +
                $"/executionLog:{input.DecompressionOut.OutputDirectory}\\Domino.xlg " +
                $"/buildQueue:{input.DecompressionOut.BuildData.BuildQueue} " +
                $"/buildId:{input.DecompressionOut.BuildData.BuildId} " +
                $"/buildStartTicks:{input.DecompressionOut.BuildData.StartTime} " +
                $"/buildDurationMs:{input.DecompressionOut.BuildData.BuildDurationMs} ";
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
            proc.Close();
        }
    }

    /// <summary>
    /// The input for this actions needs the bxl executable, the path to the domino log and some
    /// info on the build is going to analize
    /// </summary>
    public class BuildAnalisysInput
    {
        /// <summary>
        /// location of this conf file
        /// </summary>
        public string BxlAnalyzerConfigFile { get; set; }
        /// <summary>
        /// the decompression output, that has a path
        /// </summary>
        public DecompressionOutput DecompressionOut { get; set; }
        /// <summary>
        /// base constructor
        /// </summary>
        public BuildAnalisysInput(string bxlConf, DecompressionOutput output)
        {
            BxlAnalyzerConfigFile = bxlConf;
            DecompressionOut = output;
        }
    }

    public class BuildAnalisysOutput
    {
        
    }
}
