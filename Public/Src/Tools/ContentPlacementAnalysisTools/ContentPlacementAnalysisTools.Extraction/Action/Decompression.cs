using System;
using System.IO;
using ContentPlacementAnalysisTools.Core.Kusto;
using ContentPlacementAnalysisTools.Core.Utils;
using ContentPlacementAnalysisTools.Extraction.Main;
using ICSharpCode.SharpZipLib.Zip;

namespace ContentPlacementAnalysisTools.Extraction.Action
{
    /// <summary>
    /// This is the action that downloads a single build
    /// </summary>
    public class Decompression : TimedAction<BuildDownloadOutput, DecompressionOutput>
    {
        private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly ApplicationConfiguration m_configuration = null;

        /// <summary>
        /// Constructor
        /// </summary>
        public Decompression(ApplicationConfiguration config)
        {
            m_configuration = config;
        }

        /// <inheritdoc />
        protected override void CleanUp(BuildDownloadOutput input, DecompressionOutput output){}
        /// <summary>
        /// Decompresses the result of a build download
        /// </summary>
        protected override DecompressionOutput Perform(BuildDownloadOutput input)
        {
            s_logger.Debug($"Decompressor starts...");
            try
            {
                // decompress the files given. We should have two in the main dir
                // there are two zip files here
                DecompressAndDelete(input.BxlZipOutputDirectory, Directory.GetParent(input.BxlZipOutputDirectory).FullName);
                DecompressAndDelete(input.DominoZipOutputDirectory, Directory.GetParent(input.DominoZipOutputDirectory).FullName);
                return new DecompressionOutput(input.OutputDirectory, input.BuildData);
            }
            catch(Exception e)
            {
                s_logger.Error(e, "An error ocurred while decompressing files, deleting...");
                Directory.Delete(input.OutputDirectory, true);
                throw;
            }
            finally
            {
                s_logger.Debug($"Decompressor ends in {Stopwatch.ElapsedMilliseconds}ms");
            }
        }
        private void DecompressAndDelete(string zipFile, string outputDir)
        {
            try
            {
                new FastZip().ExtractZip(zipFile, outputDir, null);
            }
            finally
            {
                File.Delete(zipFile);
            }
            
        }
        /// <inheritdoc />
        protected override void Setup(BuildDownloadOutput input){}
    }

    /// <summary>
    /// Domcpressor output contains a success flag and the path of the "machine" directory
    /// </summary>
    public class DecompressionOutput
    {
        /// <summary>
        /// Path where the decompressed downloads are located
        /// </summary>
        public string OutputDirectory { get; }
        /// <summary>
        /// Path where the decompressed downloads are located
        /// </summary>
        public KustoBuild BuildData { get; }

        /// <summary>
        /// Base constructor
        /// </summary>
        public DecompressionOutput(string outputDir, KustoBuild bd)
        {
            OutputDirectory = outputDir;
            BuildData = bd;
        }
    }
}
