using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using Google.Protobuf;

namespace BuildXL.Execution.Analyzer
{

    internal partial class Args
    {
        public Analyzer InitializeDominoInvocationAnalyzer()
        {
            string inputFilePath = null;
            string outputFilePath = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("intputFile", StringComparison.OrdinalIgnoreCase) ||
                     opt.Name.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    inputFilePath = ParseSingletonPathOption(opt, inputFilePath);
                }
                else
                {
                    throw Error("Unknown option for event stats analysis: {0}", opt.Name);
                }
            }

            return new DominoInvocationAnalyzer(GetAnalysisInput())
            {
                InputFilePath = inputFilePath,
                OutputFilePath = outputFilePath
            };
        }

        private static void WriteDominoInvocationHelp(HelpWriter writer)
        {
            writer.WriteBanner("Domino Invocation \"Analyzer\"");
            writer.WriteModeOption(nameof(AnalysisMode.DominoInvocationXLG), "Gets and outputs information related to domino invocation events from the database.");
            writer.WriteOption("inputFile", "Required. The data file to read in", shortName: "i");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer to examine Domino Invocation events that have been dumped into the db
    /// </summary>
    internal sealed class DominoInvocationAnalyzer : Analyzer
    {

        public string InputFilePath;
        public string OutputFilePath;

        public DominoInvocationAnalyzer(AnalysisInput input): base(input)
        {

        }

        public override int Analyze()
        {
            Test1 testProto;
            using (Stream stream = File.OpenRead(InputFilePath))
            {
                testProto = Test1.Parser.ParseFrom(stream);
            }

            Console.WriteLine("The name in the file is {0}", testProto.Name);
            Console.WriteLine("The other name in the file is {0}", testProto.Danny);
            return 0;
        }

    }
}
