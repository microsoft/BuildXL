// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Storage.FileContentTableAccessor;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    public class EngineStateTests : BaseEngineTest
    {
        private const string InputFilename = "foo.txt";
        private const string OutputFilename = "theoutput.txt";

        public EngineStateTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void EngineStateIsUpdated()
        {
            SetupHelloWorld();
            SetUpConfig();
            EngineState lastEngineState = RunEngine("Engine State is Updated");
            XAssert.IsTrue(EngineState.IsUsable(lastEngineState));

            var previousEngineState = lastEngineState;

            FreshSetUp();
            lastEngineState = RunEngine("Engine State is Updated again", engineState: lastEngineState);
            XAssert.IsNotNull(lastEngineState);
            XAssert.IsTrue(EngineState.IsUsable(lastEngineState));
            XAssert.IsFalse(EngineState.IsUsable(previousEngineState));
            XAssert.AreNotSame(previousEngineState, lastEngineState);
        }

        [Fact]
        public void EngineStateIsUpdatedAfterFailedExecution()
        {
            SetupHelloWorld();
            SetUpConfig();

            EngineState afterFirst = RunEngine("First build");

            FreshSetUp();
            Configuration.Filter = "tag='IDontMatchAnything'";

            // Build should fail
            var afterFail = RunEngine("Engine State is Updated", expectSuccess: false, engineState: afterFirst);
            AssertErrorEventLogged(global::BuildXL.Pips.Tracing.LogEventId.NoPipsMatchedFilter);
            XAssert.IsNotNull(afterFail);
            XAssert.IsTrue(EngineState.IsUsable(afterFail));
            XAssert.IsFalse(EngineState.IsUsable(afterFirst));
            XAssert.AreNotSame(afterFirst, afterFail);
        }

        [Fact]
        public void EngineStateIsUpdatedAfterFailedScheduling()
        {
            SetupHelloWorld();
            SetUpConfig();

            EngineState afterFirst = RunEngine("First build");

            RestartEngine();
            AddModule("HelloWorld", ("hello.dsc", "invalid spec content"), placeInRoot: true); // Invalid spec
            SetUpConfig(); 

             // Build should fail
             var afterFail = RunEngine("Engine State is Updated", expectSuccess: false, engineState: afterFirst);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.TypeScriptSyntaxError, 5);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace); 
            XAssert.IsNotNull(afterFail);
            XAssert.IsTrue(EngineState.IsUsable(afterFail));
            XAssert.IsFalse(EngineState.IsUsable(afterFirst));
            XAssert.AreNotSame(afterFirst, afterFail);
        }

        [Trait("Category", "SkipLinux")] // TODO: nothing is recorded in firstFCT
        [Fact]
        public void TestEngineStateFileContentTableReuse()
        {
            SetupHelloWorld();
            SetUpConfig();
            EngineState lastEngineState = RunEngine("First build");
            var firstFCT = lastEngineState.FileContentTable;
            XAssert.IsNotNull(firstFCT);

            FileIdAndVolumeId inFileIdentity = GetIdentity(GetFullPath(InputFilename));
            FileIdAndVolumeId outFileIdentity = GetIdentity(GetFullPath(OutputFilename));
            Usn inFileUsn = Usn.Zero;
            Usn outFileUsn = Usn.Zero;
            ISet<FileIdAndVolumeId> ids = new HashSet<FileIdAndVolumeId>();
            ISet<(FileIdAndVolumeId, string)> idsAndPaths = new HashSet<(FileIdAndVolumeId, string)>();

            XAssert.IsTrue(FileContentTableAccessorFactory.TryCreate(out var accesor, out string error));
            firstFCT.VisitKnownFiles(accesor, FileShare.ReadWrite | FileShare.Delete,
                (fileIdAndVolumeId, fileHandle, path, knownUsn, knownHash) =>
                {
                    if (fileIdAndVolumeId == inFileIdentity)
                    {
                        inFileUsn = knownUsn;
                    }
                    else if (fileIdAndVolumeId == outFileIdentity)
                    {
                        outFileUsn = knownUsn;
                    }
                    ids.Add(fileIdAndVolumeId);
                    idsAndPaths.Add((fileIdAndVolumeId, path));
                    return true;
                });

            XAssert.AreNotEqual(Usn.Zero, inFileUsn);
            XAssert.AreNotEqual(Usn.Zero, outFileUsn);

            // Run engine again
            FreshSetUp("change some stuff");
            lastEngineState = RunEngine("Second build", engineState: lastEngineState);
            var secondFCT = lastEngineState.FileContentTable;
            XAssert.AreNotSame(firstFCT, secondFCT);    // The FCT gets updated at the end of the run

            outFileIdentity = GetIdentity(GetFullPath(OutputFilename));   // Output file changed
            bool visitedInput = false;
            bool visitedOutput = false;

            secondFCT.VisitKnownFiles(accesor, FileShare.ReadWrite | FileShare.Delete,
                (fileIdAndVolumeId, fileHandle, path, knownUsn, knownHash) =>
                {

                    if (fileIdAndVolumeId == inFileIdentity)
                    {
                        XAssert.IsTrue(ids.Contains(fileIdAndVolumeId));
                        XAssert.IsTrue(inFileUsn < knownUsn);   // We modified the file 
                        inFileUsn = knownUsn;
                        visitedInput = true;
                    }
                    else if (fileIdAndVolumeId == outFileIdentity)
                    {
                        XAssert.IsFalse(ids.Contains(fileIdAndVolumeId));
                        XAssert.IsTrue(outFileUsn < knownUsn);  // New output file
                        outFileUsn = knownUsn;
                        visitedOutput = true;
                    }
                    return true;
                });

            XAssert.IsTrue(visitedInput, "visitedInput is false");
            XAssert.IsTrue(visitedOutput, "visitedOutput is false");
            XAssert.IsTrue(inFileUsn < outFileUsn, "inFileUsn > outFileUsn");
        }

        [Fact]
        public void TestMultipleReuses()
        {
            SetupHelloWorld();
            SetUpConfig();
            EngineState firstEngineState = RunEngine("First build");

            FreshSetUp("bar");
            EngineState secondEngineState = RunEngine("Second build", engineState: firstEngineState);
            XAssert.IsTrue(!EngineState.IsUsable(firstEngineState));
            XAssert.IsNotNull(secondEngineState);
            XAssert.IsTrue(!secondEngineState.IsDisposed);
            XAssert.AreNotSame(firstEngineState, secondEngineState);

            FreshSetUp("baz");
            EngineState thirdEngineState = RunEngine("Third build", engineState: secondEngineState);
            XAssert.IsTrue(!EngineState.IsUsable(secondEngineState));
            XAssert.IsNotNull(EngineState.IsUsable(thirdEngineState));
            XAssert.IsTrue(!thirdEngineState.IsDisposed);
            XAssert.AreNotSame(secondEngineState, thirdEngineState);
        }

        private void FreshSetUp(string content = "")
        {
            // Restart - emulate the server, which builds a new engine from scratch
            // This is necessary because when we get a graph cache hit the engine context gets
            // invalidated and then reloaded from the engineState, so if we are reusing the same 
            // context (i.e. if this.Context doesn't change) everything breaks.
            // We'll still have graph cache hits because the graph doesn't change 
            RestartEngine();
            SetupHelloWorld(content);
            SetUpConfig();
        }

        private static FileIdAndVolumeId GetIdentity(string filePath)
        {
            var openResult = FileUtilities.TryCreateOrOpenFile(
                filePath,
                FileDesiredAccess.GenericRead,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagOverlapped,
                out var handle);
            XAssert.IsTrue(openResult.Succeeded, $"Failed to create or open file {filePath} to get its ID. Status: {openResult.Status}");
            using (handle)
            {
                FileIdAndVolumeId? id = FileUtilities.TryGetFileIdentityByHandle(handle);
                XAssert.IsNotNull(id);
                return id.Value;
            }
        }

        private void SetUpConfig()
        {
            Configuration.Cache.CacheGraph = true;
            Configuration.Engine.ReuseEngineState = true;
            Configuration.Engine.UseFileContentTable = true;
        }

        private void SetupHelloWorld(string content = "")
        {
            WriteFile(InputFilename, $"Some content {content}");

            SetConfig($@"
config({{
    modules: [f`./HelloWorld/module.config.dsc`],
    mounts: [
        {{
            name: a`myMount`,
            path: p`{TemporaryDirectory}`,
            trackSourceFileChanges: true,
            isReadable: true,
            isWritable: true,
        }},
     ]
}});");

            var spec = @$"
import {{Artifact, Cmd, Tool, Transformer}} from 'Sdk.Transformers';

const directory = Context.getMount('myMount').path;
const foo : File = Transformer.copyFile(f`${{directory}}/{InputFilename}`, p`${{directory}}/{OutputFilename}`);
";

            AddModule("HelloWorld", ("hello.dsc", spec), placeInRoot: true);
        }
    }
}
