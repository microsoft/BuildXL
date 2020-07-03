using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Utilities
{
    public class TextWriterAdapterTests
    {
        [Fact]
        public void TestWritesInInfoMode()
        {
            var log = new RollingMemoryLog(Severity.Diagnostic);
            using var logger = new Logger(synchronous: true, log);
            var context = new Context(logger);

            var twa = new TextWriterAdapter(context, Severity.Info);
            twa.WriteLine("Hello World!");
            log.RecentEntries(1).First().Should().Contain("Hello World!");
        }

        [Fact]
        public void TestNewLinesAndSingleCharsAreIgnored()
        {
            var log = new RollingMemoryLog(Severity.Diagnostic);
            using var logger = new Logger(synchronous: true, log);
            var context = new Context(logger);

            var twa = new TextWriterAdapter(context, Severity.Info);
            twa.WriteLine();
            twa.WriteLine();
            twa.WriteLine();
            twa.Write('H');
            twa.Write('e');
            twa.Write('l');
            twa.Write('l');
            twa.Write('o');
            log.RecentEntries(1).Should().BeEmpty();
        }
    }
}
