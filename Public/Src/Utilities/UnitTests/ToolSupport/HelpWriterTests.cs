// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.IO;
using BuildXL.ToolSupport;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System;

namespace Test.BuildXL.ToolSupport
{
    public sealed class HelpWriterTests : XunitBuildXLTest
    {
        public HelpWriterTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void HelpWriter()
        {
            // when outputting to the console, just ensure things work without crashing...
            var hw = new HelpWriter();
            hw.WriteLine();
            hw.WriteLine("Hello");
            hw.WriteBanner("BANNER");
            hw.WriteOption("name", "description");

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                hw = new HelpWriter(writer, 30);
                hw.WriteLine();
                hw.WriteLine("A short line");
                hw.WriteLine("A long long long long long long line");
                hw.WriteOption(string.Empty, string.Empty);

                // test an option of length HelpWriter.DescriptionColumn - N
                hw.WriteOption("namenamenamenamenamenamename", string.Empty);
                hw.WriteOption("namenamenamenamenamenamename", "description");

                // test an option of length HelpWriter.DescriptionColumn - 1
                hw.WriteOption("namenamenamenamenamenamenamenam", string.Empty);
                hw.WriteOption("namenamenamenamenamenamenamenam", "description");

                // test an option of length HelpWriter.DescriptionColumn
                hw.WriteOption("namenamenamenamenamenamenamename", string.Empty);
                hw.WriteOption("namenamenamenamenamenamenamename", "description");

                // test an option of length HelpWriter.DescriptionColumn + 1
                hw.WriteOption("namenamenamenamenamenamenamenamen", string.Empty);
                hw.WriteOption("namenamenamenamenamenamenamenamen", "description");

                // test an option of length HelpWriter.DescriptionColumn + N
                hw.WriteOption("namenamenamenamenamenamenamenamenamenamenamename", string.Empty);
                hw.WriteOption("namenamenamenamenamenamenamenamenamenamenamename", "description");

                // test long option name and description
                hw.WriteOption(
                    "namenamenamenamenamenamenamenamenamenamenamename",
                    "descriptiondescriptiondescriptiondescriptiondescriptiondescription");
                hw.WriteBanner(string.Empty);
                hw.WriteBanner("banner");
                hw.WriteBanner("bannerbannerbannerbannerbannerbannerbanner");

                writer.Flush();
                string output = writer.ToString();

                string Expected =
                    Environment.NewLine + "A short line"+ 
                    Environment.NewLine + "A long long long long long"+
                    Environment.NewLine + "long line"+
                    Environment.NewLine + Environment.NewLine + "namenamenamenamenamenamename"+ 
                    Environment.NewLine + "namenamenamenamenamenamename    description"+
                    Environment.NewLine + "namenamenamenamenamenamenamenam"+
                    Environment.NewLine + "namenamenamenamenamenamenamenam description"+
                    Environment.NewLine + "namenamenamenamenamenamenamename"+
                    Environment.NewLine + "namenamenamenamenamenamenamename"+
                    Environment.NewLine + "                                description"+
                    Environment.NewLine + "namenamenamenamenamenamenamenamen"+
                    Environment.NewLine + "namenamenamenamenamenamenamenamen"+
                    Environment.NewLine + "                                description"+
                    Environment.NewLine + "namenamenamenamenamenamenamenamenamenamenamename"+
                    Environment.NewLine + "namenamenamenamenamenamenamenamenamenamenamename"+
                    Environment.NewLine + "                                description"+
                    Environment.NewLine + "namenamenamenamenamenamenamenamenamenamenamename"+
                    Environment.NewLine + "                                descriptiondescriptiondescriptiondescriptiondescriptiondescription"+
                    Environment.NewLine + Environment.NewLine + "             -  -"+
                    Environment.NewLine + Environment.NewLine + Environment.NewLine + "          - banner -"+
                    Environment.NewLine + Environment.NewLine + Environment.NewLine + "- bannerbannerbannerbannerbannerbannerbanner -"+
                    Environment.NewLine + Environment.NewLine;
                Assert.Equal(Expected, output);
            }
        }

        [Fact]
        public void HelpWriterShortForm()
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                var hw = new HelpWriter(writer, 150);

                // test an option of length HelpWriter.DescriptionColumn - N
                hw.WriteOption("name", string.Empty, shortName: "s");
                hw.WriteOption("name", "description", shortName: "s");

                writer.Flush();
                string output = writer.ToString();

                string Expected =
                    "name                            (Short form: /s)"+ Environment.NewLine + "name                            description (Short form: /s)" + Environment.NewLine;
                Assert.Equal(Expected, output);
            }
        }

        [Fact]
        public void HelpWriterVerbosity()
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                HelpWriter hw = new HelpWriter(writer, 30, HelpLevel.Standard);
                hw.WriteLine("Standard", HelpLevel.Standard);
                hw.WriteLine("Verbose", HelpLevel.Verbose);

                writer.Flush();
                Assert.Equal("Standard"+ Environment.NewLine, writer.ToString());
            }

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                HelpWriter hw = new HelpWriter(writer, 30, HelpLevel.Verbose);
                hw.WriteLine("Standard", HelpLevel.Standard);
                hw.WriteLine("Verbose", HelpLevel.Verbose);

                writer.Flush();
                Assert.Equal("Standard"+ Environment.NewLine + "Verbose"+ Environment.NewLine, writer.ToString());
            }
        }
    }
}
