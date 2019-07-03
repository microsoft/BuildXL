using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Engine;
using System.Threading;
using System.Diagnostics;

namespace Test.BuildXL.Pips
{
    public class RemapReaderWriterTests : XunitBuildXLTest
    {
        public RemapReaderWriterTests(ITestOutputHelper output) : base(output)
        {
        }

        public void TestInliningPath()
        {
            PathTable writerPathTable = new PathTable();
            var writerFileSystem = new PassThroughFileSystem(writerPathTable);
            PipExecutionContext writerContext = EngineContext.CreateNew(CancellationToken.None, writerPathTable, writerFileSystem);
            PipGraphFragmentContext writerFragmentContext = new PipGraphFragmentContext();

            PathTable readerPathTable = new PathTable();
            var readerFileSystem = new PassThroughFileSystem(writerPathTable);
            PipExecutionContext readerContext = EngineContext.CreateNew(CancellationToken.None, readerPathTable, readerFileSystem);
            PipGraphFragmentContext readerFragmentContext = new PipGraphFragmentContext();
            using (MemoryStream memStream = new MemoryStream())
            using (RemapWriter writer = new RemapWriter(memStream, writerContext, writerFragmentContext))
            using (RemapReader reader = new RemapReader(readerFragmentContext, memStream, readerContext))
            {
                string path = @"d:\foo";
                AbsolutePath absolutePath;
                AbsolutePath.TryCreate(writerPathTable, path, out absolutePath);
                writer.Write(absolutePath);
                writer.Write(absolutePath);

                memStream.Position = 0;

                AbsolutePath firstPath = reader.ReadAbsolutePath();
                string firstPathString = firstPath.ToString(readerPathTable);
                XAssert.AreEqual(path, firstPathString);
                AbsolutePath secondPath = reader.ReadAbsolutePath();
                XAssert.AreEqual(firstPath, secondPath);
            }
        }

        public void TestInliningString()
        {
            PathTable writerPathTable = new PathTable();
            var writerFileSystem = new PassThroughFileSystem(writerPathTable);
            PipExecutionContext writerContext = EngineContext.CreateNew(CancellationToken.None, writerPathTable, writerFileSystem);
            PipGraphFragmentContext writerFragmentContext = new PipGraphFragmentContext();

            PathTable readerPathTable = new PathTable();
            var readerFileSystem = new PassThroughFileSystem(writerPathTable);
            PipExecutionContext readerContext = EngineContext.CreateNew(CancellationToken.None, readerPathTable, readerFileSystem);
            PipGraphFragmentContext readerFragmentContext = new PipGraphFragmentContext();
            using (MemoryStream memStream = new MemoryStream())
            using (RemapWriter writer = new RemapWriter(memStream, writerContext, writerFragmentContext))
            using (RemapReader reader = new RemapReader(readerFragmentContext, memStream, readerContext))
            {
                string stringToTest = @"myrangdomstring";
                StringId stringId;
                stringId = StringId.Create(writerContext.StringTable, stringToTest);
                writer.Write(stringId);
                writer.Write(stringId);

                memStream.Position = 0;

                StringId firstStringId = reader.ReadStringId();
                string firstString = firstStringId.ToString(readerContext.StringTable);
                XAssert.AreEqual(stringToTest, firstString);
                StringId secondStringId = reader.ReadStringId();
                XAssert.AreEqual(firstStringId, secondStringId);
            }
        }

        public void TestInliningPipData()
        {
            PathTable writerPathTable = new PathTable();
            var writerFileSystem = new PassThroughFileSystem(writerPathTable);
            PipExecutionContext writerContext = EngineContext.CreateNew(CancellationToken.None, writerPathTable, writerFileSystem);
            PipGraphFragmentContext writerFragmentContext = new PipGraphFragmentContext();

            PathTable readerPathTable = new PathTable();
            var readerFileSystem = new PassThroughFileSystem(writerPathTable);
            PipExecutionContext readerContext = EngineContext.CreateNew(CancellationToken.None, readerPathTable, readerFileSystem);
            PipGraphFragmentContext readerFragmentContext = new PipGraphFragmentContext();
            using (MemoryStream memStream = new MemoryStream())
            using (RemapWriter writer = new RemapWriter(memStream, writerContext, writerFragmentContext))
            using (RemapReader reader = new RemapReader(readerFragmentContext, memStream, readerContext))
            {
                string path = @"d:\foo";
                AbsolutePath absolutePath;
                AbsolutePath.TryCreate(writerPathTable, path, out absolutePath);
                PipDataBuilder pipDataBuilder = new PipDataBuilder(writerContext.StringTable);
                pipDataBuilder.Add(path);
                pipDataBuilder.Add(absolutePath);
                PipData pipdata = pipDataBuilder.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping);
                writer.Write(pipdata);
                writer.Write(pipdata);

                memStream.Position = 0;

                PipData firstPipData = reader.ReadPipData();
                string firstPathString = firstPipData.Entries[1].GetStringValue().ToString(readerContext.StringTable);
                XAssert.AreEqual(path, firstPathString);
                string firstAbsolutePathString = firstPipData.Entries[2].GetPathValue().ToString(readerPathTable);
                XAssert.AreEqual(path, firstAbsolutePathString);
                PipData secondPipData = reader.ReadPipData();
                XAssert.AreEqual(firstPipData, secondPipData);
            }
        }
    }
}
