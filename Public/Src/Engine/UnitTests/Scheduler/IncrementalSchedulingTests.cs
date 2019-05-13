// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Storage;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public sealed partial class SchedulerTest
    {
        /// <summary>
        /// Volume map
        /// </summary>
        private VolumeMap m_volumeMap;

        /// <summary>
        /// Change journal
        /// </summary>
        private IChangeJournalAccessor m_journal;

        private void CreateBasicGraph(
            Dictionary<string, NodeId> nodes = null,
            Dictionary<string, FileArtifact> files = null,
            Dictionary<string, Process> processes = null,
            Dictionary<string, CopyFile> copies = null)
        {
            // Create a graph like:
            //             S3
            //             ^
            //             |
            //             |
            //  S1   S2   P1
            //   ^   ^   ^ ^
            //    \  |  /  |
            //     \ | /   | 
            //       P2    P3
            //       ^
            //       |
            //       |
            //       C1 (optional)
            //
            //
            //

            PipProvenance sharedProvenance = CreateProvenance();

            FileArtifact s1 = CreateSourceFile();
            FileArtifact s2 = CreateSourceFile();
            FileArtifact s3 = CreateSourceFile();

            FileArtifact o1 = CreateOutputFileArtifact();
            FileArtifact o2 = CreateOutputFileArtifact();
            FileArtifact o3 = CreateOutputFileArtifact();

            Process p1 = CreateProcess(dependencies: new[] { s3 }, outputs: new[] { o1 }, tags: new[] { "P1" }, provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p1);
            Process p2 = CreateProcess(dependencies: new[] { s1, s2, o1 }, outputs: new[] { o2 }, tags: new[] { "P2" }, provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p2);
            Process p3 = CreateProcess(dependencies: new[] { o1 }, outputs: new[] { o3 }, tags: new[] { "P3" }, provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p3);

            if (copies != null)
            {
                FileArtifact o4 = CreateOutputFileArtifact();
                CopyFile c1 = CreateCopyFile(sourceFile: o2, targetFile: o4, tags: new[] { "C1" });
                PipGraphBuilder.AddCopyFile(c1);
                copies.Add("C1", c1);
                nodes?.Add("C1", c1.PipId.ToNodeId());
                files?.Add("O4", o4);
            }

            if (nodes != null)
            {
                nodes["S1"] = PipGraphBuilder.GetProducerNode(s1);
                nodes["S2"] = PipGraphBuilder.GetProducerNode(s2);
                nodes["S3"] = PipGraphBuilder.GetProducerNode(s3);
                nodes["P1"] = p1.PipId.ToNodeId();
                nodes["P2"] = p2.PipId.ToNodeId();
                nodes["P3"] = p3.PipId.ToNodeId();
            }

            if (files != null)
            {
                files["S1"] = s1;
                files["S2"] = s2;
                files["S3"] = s3;
                files["O1"] = o1;
                files["O2"] = o2;
                files["O3"] = o3;
            }

            if (processes != null)
            {
                processes["P1"] = p1;
                processes["P2"] = p2;
                processes["P3"] = p3;
            }
        }

        private void CreateGraphWithDirectoryDependenciesAndOutputs(
            Dictionary<string, NodeId> nodes = null,
            Dictionary<string, FileArtifact> files = null,
            Dictionary<string, DirectoryArtifact> directories = null,
            Dictionary<string, Process> processes = null)
        {
            // S1
            //  ^
            //  |
            //  P1
            //  ^
            //  |
            //  D1     S2
            //  ^      ^
            //  |      |
            //  P2 ----+
            //  ^
            //  |
            //  O2 <--- P3 <--- O3

            PipProvenance sharedProvenance = CreateProvenance();
            FileArtifact s1 = CreateSourceFile();
            FileArtifact s2 = CreateSourceFile();
            AbsolutePath d1 = CreateUniqueObjPath("D1");
            FileArtifact o2 = CreateOutputFileArtifact();
            FileArtifact o3 = CreateOutputFileArtifact();
            FileArtifact d1O1 = FileArtifact.CreateOutputFile(d1.Combine(Context.PathTable, "O1"));
            FileArtifact d1O2 = FileArtifact.CreateOutputFile(d1.Combine(Context.PathTable, "O2"));
            var sealedOutputDirectories = new Dictionary<AbsolutePath, DirectoryArtifact>();

            Process p1 = CreateProcess(
                dependencies: new[] {s1},
                outputs: new FileArtifact[0],
                outputDirectoryPaths: new[] {d1},
                directoryOutputsToProduce: new[] {d1O1, d1O2},
                resultingSealedOutputDirectories: sealedOutputDirectories,
                tags: new[] {"P1"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p1);

            Process p2 = CreateProcess(
                dependencies: new[] {s2},
                outputs: new[] {o2},
                directoryDependencies: new[] {sealedOutputDirectories[d1]},
                directoryDependenciesToConsume: new[] {d1O1},
                tags: new[] {"P2"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p2);

            Process p3 = CreateProcess(
                dependencies: new[] {o2},
                outputs: new[] {o3},
                tags: new[] {"P3"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p3);

            if (nodes != null)
            {
                nodes["S1"] = PipGraphBuilder.GetProducerNode(s1);
                nodes["S2"] = PipGraphBuilder.GetProducerNode(s2);
                nodes["P1"] = p1.PipId.ToNodeId();
                nodes["P2"] = p2.PipId.ToNodeId();
                nodes["P3"] = p3.PipId.ToNodeId();
            }

            if (files != null)
            {
                files["S1"] = s1;
                files["S2"] = s2;
                files["D1/O1"] = d1O1;
                files["D1/O2"] = d1O2;
                files["O2"] = o2;
                files["O3"] = o3;
            }

            if (directories != null)
            {
                directories["D1"] = sealedOutputDirectories[d1];
            }

            if (processes != null)
            {
                processes["P1"] = p1;
                processes["P2"] = p2;
                processes["P3"] = p3;
            }
        }

        private void CreateGraphWithSealedDirectory(
            Dictionary<string, NodeId> nodes = null,
            Dictionary<string, FileArtifact> files = null,
            Dictionary<string, DirectoryArtifact> directories = null,
            Dictionary<string, Process> processes = null)
        {
            // SD/F1 SD/F2    S3
            // ^       ^      ^
            // |       |      |
            // SD -----+      |
            // ^              |
            // |              |
            // +--------------+
            //       |
            //       P1
            //       ^
            //       |
            //       O1

            PipProvenance sharedProvenance = CreateProvenance();
            AbsolutePath sdPath = CreateUniqueDirectory(ReadonlyRoot);
            FileArtifact sdF1 = CreateSourceFile(sdPath.ToString(Context.PathTable));
            FileArtifact sdF2 = CreateSourceFile(sdPath.ToString(Context.PathTable));
            FileArtifact s3 = CreateSourceFile();
            FileArtifact o1 = CreateOutputFileArtifact();

            SealDirectory sdPip = CreateSealDirectory(sdPath, SealDirectoryKind.Full, sdF1, sdF2);
            DirectoryArtifact sd = PipGraphBuilder.AddSealDirectory(sdPip);

            Process p1 = CreateProcess(
                dependencies: new[] {s3},
                directoryDependencies: new[] {sd},
                directoryDependenciesToConsume: new[] {sdF1},
                outputs: new[] {o1},
                tags: new[] {"P1"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p1);

            if (nodes != null)
            {
                nodes["SD"] = sdPip.PipId.ToNodeId();
                nodes["SD/F1"] = PipGraphBuilder.GetProducerNode(sdF1);
                nodes["SD/F2"] = PipGraphBuilder.GetProducerNode(sdF2);
                nodes["S3"] = PipGraphBuilder.GetProducerNode(s3);
                nodes["P1"] = p1.PipId.ToNodeId();
            }

            if (files != null)
            {
                files["SD/F1"] = sdF1;
                files["SD/F2"] = sdF2;
                files["S3"] = s3;
                files["O1"] = o1;
            }

            if (directories != null)
            {
                directories["SD"] = sd;
            }

            if (processes != null)
            {
                processes["P1"] = p1;
            }
        }

        private void CreateGraphWithSealedDirectoryWhoseMemberIsConsumedDirectly(
            Dictionary<string, NodeId> nodes = null,
            Dictionary<string, FileArtifact> files = null,
            Dictionary<string, DirectoryArtifact> directories = null,
            Dictionary<string, Process> processes = null)
        {
            // SD/F1 SD/F2
            // ^       ^
            // |       |
            // SD -----+ -----P2 <-- O2
            // ^
            // |
            // +------
            //       |
            //       P1---> S3
            //       ^
            //       |
            //       O1

            AbsolutePath sdPath = CreateUniqueDirectory(ReadonlyRoot);
            FileArtifact sdF1 = CreateSourceFile(sdPath.ToString(Context.PathTable));
            FileArtifact sdF2 = CreateSourceFile(sdPath.ToString(Context.PathTable));
            FileArtifact s3 = CreateSourceFile();
            FileArtifact o1 = CreateOutputFileArtifact();
            FileArtifact o2 = CreateOutputFileArtifact();

            SealDirectory sdPip = CreateSealDirectory(sdPath, SealDirectoryKind.Full, sdF1, sdF2);
            DirectoryArtifact sd = PipGraphBuilder.AddSealDirectory(sdPip);

            Process p1 = CreateProcess(
                dependencies: new[] { s3 },
                directoryDependencies: new[] { sd },
                directoryDependenciesToConsume: new[] { sdF1 },
                outputs: new[] { o1 },
                tags: new[] { "P1" },
                provenance: CreateProvenance());
            PipGraphBuilder.AddProcess(p1);

            Process p2 = CreateProcess(
                dependencies: new[] { sdF2 },
                outputs: new[] { o2 },
                tags: new[] { "P2" },
                provenance: CreateProvenance());
            PipGraphBuilder.AddProcess(p2);

            if (nodes != null)
            {
                nodes["SD"] = sdPip.PipId.ToNodeId();
                nodes["SD/F1"] = PipGraphBuilder.GetProducerNode(sdF1);
                nodes["SD/F2"] = PipGraphBuilder.GetProducerNode(sdF2);
                nodes["S3"] = PipGraphBuilder.GetProducerNode(s3);
                nodes["P1"] = p1.PipId.ToNodeId();
                nodes["P2"] = p2.PipId.ToNodeId();
            }

            if (files != null)
            {
                files["SD/F1"] = sdF1;
                files["SD/F2"] = sdF2;
                files["S3"] = s3;
                files["O1"] = o1;
                files["O2"] = o2;
            }

            if (directories != null)
            {
                directories["SD"] = sd;
            }

            if (processes != null)
            {
                processes["P1"] = p1;
                processes["P2"] = p2;
            }
        }

        private void CreateGraphWithSealedSourceDirectory(
            Dictionary<string, NodeId> nodes = null,
            Dictionary<string, FileArtifact> files = null,
            Dictionary<string, DirectoryArtifact> directories = null,
            Dictionary<string, Process> processes = null)
        {
            // SSD     SSD/F1     SSD/F2
            //   ^       ^          ^
            //   |       |          | 
            //   P1      P2         P3
            //   ^       ^          ^
            //   |       |          |
            //   O1      O2         O3

            PipProvenance sharedProvenance = CreateProvenance();
            AbsolutePath ssdPath = CreateUniqueDirectory(ReadonlyRoot);
            FileArtifact ssdF1 = CreateSourceFile(ssdPath.ToString(Context.PathTable));
            FileArtifact ssdF2 = CreateSourceFile(ssdPath.ToString(Context.PathTable));

            SealDirectory ssdPip = CreateSealDirectory(ssdPath, SealDirectoryKind.SourceAllDirectories);
            DirectoryArtifact ssd = PipGraphBuilder.AddSealDirectory(ssdPip);

            FileArtifact o1 = CreateOutputFileArtifact();
            FileArtifact o2 = CreateOutputFileArtifact();
            FileArtifact o3 = CreateOutputFileArtifact();

            Process p1 = CreateProcess(
                dependencies: new FileArtifact[0],
                directoryDependencies: new[] {ssd},
                directoryDependenciesToConsume: new[] {ssdF1},
                outputs: new[] {o1},
                tags: new[] {"P1"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p1);

            Process p2 = CreateProcess(
                dependencies: new[] {ssdF1},
                outputs: new[] {o2},
                tags: new[] {"P2"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p2);

            Process p3 = CreateProcess(
                dependencies: new[] {ssdF2},
                outputs: new[] {o3},
                tags: new[] {"P3"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p3);

            if (nodes != null)
            {
                nodes["SSD"] = ssdPip.PipId.ToNodeId();
                nodes["SSD/F1"] = PipGraphBuilder.GetProducerNode(ssdF1);
                nodes["SSD/F2"] = PipGraphBuilder.GetProducerNode(ssdF2);
                nodes["P1"] = p1.PipId.ToNodeId();
                nodes["P2"] = p2.PipId.ToNodeId();
                nodes["P3"] = p3.PipId.ToNodeId();
            }

            if (files != null)
            {
                files["SSD/F1"] = ssdF1;
                files["SSD/F2"] = ssdF2;
                files["O1"] = o1;
                files["O2"] = o2;
                files["O3"] = o3;
            }

            if (directories != null)
            {
                directories["SSD"] = ssd;
            }

            if (processes != null)
            {
                processes["P1"] = p1;
                processes["P2"] = p2;
                processes["P3"] = p3;
            }
        }

        private void CreateGraphWithSealedSourceAndOutputDirectories(
            Dictionary<string, NodeId> nodes = null,
            Dictionary<string, FileArtifact> files = null,
            Dictionary<string, DirectoryArtifact> directories = null,
            Dictionary<string, Process> processes = null)
        {
            // SSD/F1
            //   ^
            //   |
            //   P1              S1
            //   ^               ^
            //   |               |
            //   + ----- O1 <--- P2 <--- O2
            //   |
            //   D1

            PipProvenance sharedProvenance = CreateProvenance();
            AbsolutePath ssdPath = CreateUniqueDirectory(ReadonlyRoot);
            FileArtifact ssdF1 = CreateSourceFile(ssdPath.ToString(Context.PathTable));

            SealDirectory ssdPip = CreateSealDirectory(ssdPath, SealDirectoryKind.SourceAllDirectories);
            DirectoryArtifact ssd = PipGraphBuilder.AddSealDirectory(ssdPip);

            FileArtifact s1 = CreateSourceFile();
            FileArtifact o1 = CreateOutputFileArtifact();
            FileArtifact o2 = CreateOutputFileArtifact();

            AbsolutePath d1 = CreateUniqueObjPath("D1");
            FileArtifact d1O1 = FileArtifact.CreateOutputFile(d1.Combine(Context.PathTable, "O1"));
            FileArtifact d1O2 = FileArtifact.CreateOutputFile(d1.Combine(Context.PathTable, "O2"));
            var sealedOutputDirectories = new Dictionary<AbsolutePath, DirectoryArtifact>();

            Process p1 = CreateProcess(
                dependencies: new FileArtifact[0],
                directoryDependencies: new[] {ssd},
                directoryDependenciesToConsume: new[] {ssdF1},
                outputs: new[] {o1},
                outputDirectoryPaths: new[] {d1},
                directoryOutputsToProduce: new[] {d1O1, d1O2},
                resultingSealedOutputDirectories: sealedOutputDirectories,
                tags: new[] {"P1"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p1);

            Process p2 = CreateProcess(
                dependencies: new[] {s1, o1},
                outputs: new[] {o2},
                tags: new[] {"P2"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p2);

            if (nodes != null)
            {
                nodes["SSD"] = ssdPip.PipId.ToNodeId();
                nodes["P1"] = p1.PipId.ToNodeId();
                nodes["P2"] = p2.PipId.ToNodeId();
            }

            if (files != null)
            {
                files["S1"] = s1;
                files["SSD/F1"] = ssdF1;
                files["O1"] = o1;
                files["O2"] = o2;
                files["D1/O1"] = d1O1;
                files["D1/O2"] = d1O2;
            }

            if (directories != null)
            {
                directories["SSD"] = ssd;
                directories["D1"] = sealedOutputDirectories[d1];
            }

            if (processes != null)
            {
                processes["P1"] = p1;
                processes["P2"] = p2;
            }
        }

        private void CreateGraphWithPipThatEnumeratesNonSealedDirectory(
            Dictionary<string, AbsolutePath> filesAndDirectories,
            out NodeId processNode,
            out Process process)
        {
            // Create D, D/E1, and D/E2
            var d = CreateUniqueDirectory(ReadonlyRoot);
            var dE1 = CreateUniqueDirectory(d.ToString(Context.PathTable));
            var dE2 = CreateUniqueDirectory(d.ToString(Context.PathTable));

            // Create D/E1/F1.txt, D/E2/F1.txt, D/E2/F2.txt
            var dE1F1Txt = CreateSourceFile(dE1.ToString(Context.PathTable));
            var dE2F1Txt = CreateSourceFile(dE2.ToString(Context.PathTable));
            var dE2F2Txt = CreateSourceFile(dE2.ToString(Context.PathTable));

            // Declare D/E1/F2.txt but don't create it.
            var dE1F2Txt = CreateUniqueSourcePath(SourceRootPrefix, dE1.ToString(Context.PathTable));

            // Create b.bat.
            var bBat = FileArtifact.CreateSourceFile(d.GetParent(Context.PathTable).Combine(Context.PathTable, "B.bat"));
            File.WriteAllText(
                bBat.Path.ToString(Context.PathTable),
                @"@ECHO OFF

SET InputDirectory=%1
SET OutputFile=%2

FOR /F %%f IN ('dir /b /s ""%InputDirectory%\*""') DO echo %%f >> %OutputFile%

IF ERRORLEVEL 1 GOTO error
ENDLOCAL && EXIT /b 0
:error
ENDLOCAL && EXIT /b 1
");

            // Create OUTPUT
            var output = CreateOutputFileArtifact();

            if (filesAndDirectories != null)
            {
                filesAndDirectories["D"] = d;
                filesAndDirectories["D/E1"] = dE1;
                filesAndDirectories["D/E2"] = dE2;
                filesAndDirectories["D/E1/F1.txt"] = dE1F1Txt.Path;
                filesAndDirectories["D/E1/F2.txt"] = dE1F2Txt;
                filesAndDirectories["D/E2/F1.txt"] = dE2F1Txt.Path;
                filesAndDirectories["D/E2/F2.txt"] = dE2F2Txt.Path;
                filesAndDirectories["B.bat"] = bBat.Path;
                filesAndDirectories["OUTPUT"] = output.Path;
            }

            var cmdBuilder = NewCmdProcessBuilder();

            // cmd.exe /d /c b.bat D OUTPUT
            PipDataBuilder pipDataBuilder = new PipDataBuilder(Context.PathTable.StringTable);
            pipDataBuilder.Add("/d");
            pipDataBuilder.Add("/c");
            using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
            {
                pipDataBuilder.Add(bBat);
                pipDataBuilder.Add(d);
                pipDataBuilder.Add(output);
            }

            cmdBuilder.WithArguments(pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules));
            cmdBuilder.WithDependencies(bBat);
            cmdBuilder.WithOutputs(output);

            process = cmdBuilder.Build();
            PipGraphBuilder.AddProcess(process);
            processNode = process.PipId.ToNodeId();
        }

        private void CreateSimpleGraphWherePipDependsOnTwoProducers(
            Dictionary<string, NodeId> nodes = null,
            Dictionary<string, FileArtifact> files = null,
            Dictionary<string, Process> processes = null)
        {
            // S1              S2
            // ^               ^
            // |               |
            // P1              P2
            // ^               ^
            // |               |
            // O1 <----+-----> O2
            //         |
            //         P3
            //         ^
            //         |
            //         O3

            PipProvenance sharedProvenance = CreateProvenance();

            FileArtifact s1 = CreateSourceFile();
            FileArtifact s2 = CreateSourceFile();
            
            FileArtifact o1 = CreateOutputFileArtifact();
            FileArtifact o2 = CreateOutputFileArtifact();
            FileArtifact o3 = CreateOutputFileArtifact();

            Process p1 = CreateProcess(dependencies: new[] {s1}, outputs: new[] {o1}, tags: new[] {"P1"}, provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p1);
            Process p2 = CreateProcess(dependencies: new[] {s2}, outputs: new[] {o2}, tags: new[] {"P2"}, provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p2);
            Process p3 = CreateProcess(dependencies: new[] {o1, o2}, outputs: new[] {o3}, tags: new[] {"P3"}, provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p3);

            if (nodes != null)
            {
                nodes["S1"] = PipGraphBuilder.GetProducerNode(s1);
                nodes["S2"] = PipGraphBuilder.GetProducerNode(s2);
                nodes["P1"] = p1.PipId.ToNodeId();
                nodes["P2"] = p2.PipId.ToNodeId();
                nodes["P3"] = p3.PipId.ToNodeId();
            }

            if (files != null)
            {
                files["S1"] = s1;
                files["S2"] = s2;
                files["O1"] = o1;
                files["O2"] = o2;
                files["O3"] = o3;
            }

            if (processes != null)
            {
                processes["P1"] = p1;
                processes["P2"] = p2;
                processes["P3"] = p3;
            }
        }

        private void CreateGraphWithSealingPipOutput(
            Dictionary<string, NodeId> nodes = null,
            Dictionary<string, FileArtifact> files = null,
            Dictionary<string, DirectoryArtifact> directories = null,
            Dictionary<string, Process> processes = null)
        {
            // S1
            // ^
            // |
            // P1 <-- O2     S2
            // ^             ^
            // |             |
            // O1 <-- SD <-- P2 <-- O3

            PipProvenance sharedProvenance = CreateProvenance();

            FileArtifact s1 = CreateSourceFile();
            FileArtifact s2 = CreateSourceFile();

            FileArtifact o1 = CreateOutputFileArtifact();
            FileArtifact o2 = CreateOutputFileArtifact();
            FileArtifact o3 = CreateOutputFileArtifact();

            Process p1 = CreateProcess(dependencies: new[] {s1}, outputs: new[] {o1, o2}, tags: new[] {"P1"}, provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p1);

            SealDirectory sdPip = CreateSealDirectory(o1.Path.GetParent(Context.PathTable), SealDirectoryKind.Partial, o1);
            DirectoryArtifact sd = PipGraphBuilder.AddSealDirectory(sdPip);

            Process p2 = CreateProcess(
                dependencies: new[] {s2},
                directoryDependencies: new[] {sd},
                directoryDependenciesToConsume: new[] {o1},
                outputs: new[] {o3},
                tags: new[] {"P2"},
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p2);

            if (nodes != null)
            {
                nodes["P1"] = p1.PipId.ToNodeId();
                nodes["SD"] = sdPip.PipId.ToNodeId();
                nodes["P2"] = p2.PipId.ToNodeId();
            }

            if (files != null)
            {
                files["S1"] = s1;
                files["S2"] = s2;
                files["O1"] = o1;
                files["O2"] = o2;
                files["O3"] = o3;
            }

            if (directories != null)
            {
                directories["SD"] = sd;
            }

            if (processes != null)
            {
                processes["P1"] = p1;
                processes["P2"] = p2;
            }
        }

        private void CreateGraphForTestingLazyWriteFileMaterialization(Dictionary<string, Pip> pips)
        {
            // W
            // ^
            // |
            // O1
            // ^
            // |
            // C1 <-- O2 <-- C2 <-- O3

            FileArtifact o1 = CreateOutputFileArtifact();
            var w = CreateAndScheduleWriteFile(o1, string.Empty, new[] {"Test"});

            FileArtifact o2 = CreateOutputFileArtifact();
            var c1 = CreateAndScheduleCopyFile(w.Destination, o2);

            FileArtifact o3 = CreateOutputFileArtifact();
            var c2 = CreateAndScheduleCopyFile(c1.Destination, o3, string.Empty, new[] {"A"});

            if (pips != null)
            {
                pips["W"] = w;
                pips["C1"] = c1;
                pips["C2"] = c2;
            }
        }

        private void CreateGraphForTestingWriteFile(Dictionary<string, Pip> pips, Dictionary<string, FileArtifact> files)
        {
            // O1 <- P1 <- X/Y/WO1 <- W1
            // O2 <- P2 <- X/Y/WO2 <- W2
            // O3 <- P3 <- WO3 <- W3

            PipProvenance sharedProvenance = CreateProvenance();

            string xyRoot = Path.Combine(ObjectRoot, "X", "Y");
            FileArtifact xyWO1 = CreateOutputFileArtifact(xyRoot);
            FileArtifact xyWO2 = CreateOutputFileArtifact(xyRoot);
            FileArtifact wO3 = CreateOutputFileArtifact();
            WriteFile w1 = CreateAndScheduleWriteFile(xyWO1, string.Empty, new[] { Guid.NewGuid().ToString() });
            WriteFile w2 = CreateAndScheduleWriteFile(xyWO2, string.Empty, new[] { Guid.NewGuid().ToString() });
            WriteFile w3 = CreateAndScheduleWriteFile(wO3, string.Empty, new[] { Guid.NewGuid().ToString() });

            FileArtifact o1 = CreateOutputFileArtifact();
            FileArtifact o2 = CreateOutputFileArtifact();
            FileArtifact o3 = CreateOutputFileArtifact();

            Process p1 = CreateProcess(
                dependencies: new[] { xyWO1 },
                outputs: new[] { o1 },
                tags: new[] { "P1" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p1);

            Process p2 = CreateProcess(
                dependencies: new[] { xyWO2 },
                outputs: new[] { o2 },
                tags: new[] { "P2" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p2);

            Process p3 = CreateProcess(
                dependencies: new[] { wO3 },
                outputs: new[] { o3 },
                tags: new[] { "P3" },
                provenance: sharedProvenance);
            PipGraphBuilder.AddProcess(p3);

            if (pips != null)
            {
                pips["W1"] = w1;
                pips["W2"] = w2;
                pips["W3"] = w3;
                pips["P1"] = p1;
                pips["P2"] = p2;
                pips["P3"] = p3;
            }

            if (files != null)
            {
                files["X/Y/WO1"] = xyWO1;
                files["X/Y/WO2"] = xyWO2;
                files["WO3"] = wO3;
                files["O1"] = o1;
                files["O2"] = o2;
                files["O3"] = o3;
            }
        }

        private void ModifyFile(AbsolutePath filePath, string content = null)
        {
            File.AppendAllText(filePath.ToString(Context.PathTable), content ?? Guid.NewGuid().ToString());
        }

        private void DeleteFile(AbsolutePath filePath)
        {
            if (FileExists(filePath))
            {
                File.Delete(filePath.ToString(Context.PathTable));
            }
        }

        private void DeleteDirectory(AbsolutePath directoryPath, bool recursive)
        {
            if (DirectoryExists(directoryPath))
            {
                Directory.Delete(directoryPath.ToString(Context.PathTable), recursive);
            }
        }

        private bool FileExists(AbsolutePath filePath)
        {
            return File.Exists(filePath.ToString(Context.PathTable));
        }

        private bool DirectoryExists(AbsolutePath directoryPath)
        {
            return Directory.Exists(directoryPath.ToString(Context.PathTable));
        }

        private string CreateFileInDirectory(AbsolutePath directoryPath, string fileName, string content = null)
        {
            var pathToNewFile = directoryPath.Combine(Context.PathTable, fileName).ToString(Context.PathTable);
            File.WriteAllText(pathToNewFile, content ?? Guid.NewGuid().ToString());
            return pathToNewFile;
        }
        
        private IIncrementalSchedulingState CreateNewState(FileEnvelopeId token, PipGraph pipGraph)
        {
            var factory = new IncrementalSchedulingStateFactory(LoggingContext);
            return factory.CreateNew(
                token,
                pipGraph,
                m_configuration,
                UnsafeOptions.PreserveOutputsNotUsed);
        }

        private void LoadStateAndProcessFileChanges(
            PipGraph pipGraph,
            string fileChangeTrackerPath,
            string incrementalSchedulingStatePath,
            string engineFingerprint,
            out FileChangeTracker fileChangeTracker,
            out IIncrementalSchedulingState incrementalSchedulingState)
        {
            FileChangeTracker.ResumeOrRestartTrackingChanges(LoggingContext, m_volumeMap, m_journal, fileChangeTrackerPath, engineFingerprint, out fileChangeTracker);
            XAssert.IsTrue(fileChangeTracker.IsTrackingChanges);

            var factory = new IncrementalSchedulingStateFactory(LoggingContext);

            incrementalSchedulingState = factory.LoadOrReuse(
                fileChangeTracker.FileEnvelopeId,
                pipGraph,
                m_configuration,
                UnsafeOptions.PreserveOutputsNotUsed,
                incrementalSchedulingStatePath,
                schedulerState: null);

            var fileChangeProcessor = new FileChangeProcessor(LoggingContext, fileChangeTracker);
            fileChangeProcessor.Subscribe(incrementalSchedulingState);

            var scanningJournalResult = fileChangeProcessor.TryProcessChanges();
            XAssert.IsTrue(scanningJournalResult.Succeeded);
        }

        private void IncrementalSchedulingSetup(bool enableGraphAgnosticIncrementalScheduling = true, bool enableLazyOutputMaterialization = true)
        {
            Setup(
                enableJournal: true, 
                enableIncrementalScheduling: true, 
                enableGraphAgnosticIncrementalScheduling: enableGraphAgnosticIncrementalScheduling,
                disableLazyOutputMaterialization: !enableLazyOutputMaterialization);

            IgnoreWarnings();
        }

        /// <summary>
        /// Verify that dirtying a node will dirty all dependents.
        /// </summary>
        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void TransitiveDirty(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            CreateBasicGraph(nodes);
            var pipGraph = PipGraphBuilder.Build();

            IIncrementalSchedulingState iss = CreateNewState(FileEnvelopeId.Create(), pipGraph);

            // Clear all nodes
            foreach (var n in iss.DirtyNodeTracker.AllDirtyNodes)
            {
                iss.DirtyNodeTracker.MarkNodeClean(n);
            }

            XAssert.AreEqual(0, iss.DirtyNodeTracker.AllDirtyNodes.Count());

            // Dirty the S3 node. This should dirty all processes, so the count should be 4
            iss.DirtyNodeTracker.MarkNodeDirty(nodes["S3"]);
            XAssert.AreEqual(4, iss.DirtyNodeTracker.AllDirtyNodes.Intersect(nodes.Values).Count());
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(nodes["P3"]));

            // Clear all nodes
            foreach (var n in iss.DirtyNodeTracker.AllDirtyNodes)
            {
                iss.DirtyNodeTracker.MarkNodeClean(n);
            }

            // Now dirty S1. This should only make P2 dirty.
            iss.DirtyNodeTracker.MarkNodeDirty(nodes["S1"]);
            XAssert.AreEqual(2, iss.DirtyNodeTracker.AllDirtyNodes.Intersect(nodes.Values).Count());
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeDirty(nodes["P3"]));
        }

        /// <summary>
        /// Verify that dirtying a node will dirty all dependents.
        /// </summary>
        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void PerpetualDirty(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            CreateBasicGraph(nodes);
            var pipGraph = PipGraphBuilder.Build();

            IIncrementalSchedulingState iss = CreateNewState(FileEnvelopeId.Create(), pipGraph);

            // Clear all nodes
            foreach (var n in iss.DirtyNodeTracker.AllDirtyNodes)
            {
                iss.DirtyNodeTracker.MarkNodeClean(n);
            }

            XAssert.AreEqual(0, iss.DirtyNodeTracker.AllDirtyNodes.Count());

            // Mark S3 as perpetual dirty.
            iss.DirtyNodeTracker.MarkNodePerpetuallyDirty(nodes["S3"]);
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodePerpetualDirty(nodes["S3"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(nodes["S3"]));

            // Clear all nodes
            foreach (var n in iss.DirtyNodeTracker.AllDirtyNodes)
            {
                iss.DirtyNodeTracker.MarkNodeClean(n);
            }

            // S3 should remain dirty.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodePerpetualDirty(nodes["S3"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(nodes["S3"]));
        }

        /// <summary>
        /// Verify that dirtying a node will dirty all dependents.
        /// </summary>
        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void PerpetualDirtySurvivesSaving(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            CreateBasicGraph(nodes);
            var pipGraph = PipGraphBuilder.Build();

            IIncrementalSchedulingState iss = CreateNewState(FileEnvelopeId.Create(), pipGraph);

            // Clear all nodes
            foreach (var n in iss.DirtyNodeTracker.AllDirtyNodes)
            {
                iss.DirtyNodeTracker.MarkNodeClean(n);
            }

            XAssert.AreEqual(0, iss.DirtyNodeTracker.AllDirtyNodes.Count());

            // Mark S3 as perpetual dirty.
            iss.DirtyNodeTracker.MarkNodePerpetuallyDirty(nodes["S3"]);
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodePerpetualDirty(nodes["S3"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(nodes["S3"]));

            var dirtyFile = CreateUniqueObjPath("dirty").ToString(Context.PathTable);

            var saveToken = FileEnvelopeId.Create();
            iss.SaveIfChanged(saveToken, dirtyFile);

            var factory = new IncrementalSchedulingStateFactory(LoggingContext);

            var loadedIss = factory.LoadOrReuse(
                saveToken,
                pipGraph,
                m_configuration,
                UnsafeOptions.PreserveOutputsNotUsed,
                dirtyFile,
                schedulerState: null);

            XAssert.IsNotNull(loadedIss);

            XAssert.IsTrue(loadedIss.DirtyNodeTracker.IsNodePerpetualDirty(nodes["S3"]));
            XAssert.IsTrue(loadedIss.DirtyNodeTracker.IsNodeDirty(nodes["S3"]));
        }

        /// <summary>
        /// Save clean Incremental Scheduling State, change file, reload and see if the change is detected.
        /// </summary>
        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void CheckChangesWithIncrementalState(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();

            CreateBasicGraph(nodes, files);

            var pipGraph = PipGraphBuilder.Build();
            FileChangeTracker tracker = FileChangeTracker.StartTrackingChanges(LoggingContext, m_volumeMap, m_journal, null);
            IIncrementalSchedulingState iss = CreateNewState(tracker.FileEnvelopeId, pipGraph);

            // Clear all nodes
            foreach (var n in iss.DirtyNodeTracker.AllDirtyNodes)
            {
                iss.DirtyNodeTracker.MarkNodeClean(n);
            }

            XAssert.AreEqual(0, iss.DirtyNodeTracker.AllDirtyNodes.Count());

            string changeTrackingState = GetFullPath("changeTrackingState");
            string dirtyNodeState = GetFullPath("dirtyNodeState");

            // Track S2
            string s2Path = files["S2"].Path.ToString(Context.PathTable);
            using (var s2Stream = File.OpenRead(s2Path))
            {
                XAssert.IsNotNull(s2Stream.SafeFileHandle);
                // ReSharper disable once AssignNullToNotNullAttribute
                XAssert.IsTrue(tracker.TryTrackChangesToFile(s2Stream.SafeFileHandle, s2Path).IsValid);
            }

            FileEnvelopeId savedToken = tracker.GetFileEnvelopeToSaveWith();
            bool saved = tracker.SaveTrackingStateIfChanged(changeTrackingState, savedToken);
            XAssert.IsTrue(saved);

            saved = iss.SaveIfChanged(savedToken, dirtyNodeState);
            XAssert.IsTrue(saved);

            // Modify S2
            ModifyFile(files["S2"]);

            // Load the state. The S2 node should now be dirty, along with P2
            LoadStateAndProcessFileChanges(pipGraph, changeTrackingState, dirtyNodeState, null, out tracker, out iss);
            XAssert.IsNotNull(iss);

            XAssert.AreEqual(2, iss.DirtyNodeTracker.AllDirtyNodes.Intersect(nodes.Values).Count());
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(nodes["S2"]));
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeDirty(nodes["P3"]));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestSchedulingWithIncrementalBuildWithPropagation(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateBasicGraph(nodes, files, processes);
            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            // All processes have materialized their outputs.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P3"]));

            // Modifying S1 should only cause P2 to run.
            ModifyFile(files["S1"]);

            RootFilter filter = CreateFilterForTags(
                new[] {StringId.Create(Context.PathTable.StringTable, "P2")},
                new StringId[] {});

            runScheduler = await RunScheduler(filter);
            XAssert.IsTrue(runScheduler);

            // P1 is done because it ensures that its output is hashed.
            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"));

            // P3 is filtered out.
            ExpectPipsNotDone(LabelProcess(processes, "P3"));
        }

        /// <summary>
        /// Simple filter with dependency. P1 and P3 will be scheduled when S3 is dirty.
        /// P2 is not a part in the filter.
        /// </summary>
        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestSchedulingWithIncrementalBuild(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateBasicGraph(nodes, files, processes);
            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            // All processes have materialized their outputs.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P3"]));

            // Modifying S3 should cause P1 to be rebuilt. P2 and P3 are dependents and
            // will be rebuilt without a filter. Asking for P3 will only rebuild P1 and P3
            ModifyFile(files["S3"]);

            RootFilter filter = CreateFilterForTags(
                new[] {StringId.Create(Context.PathTable.StringTable, "P3")},
                new StringId[] {});

            runScheduler = await RunScheduler(filter);
            XAssert.IsTrue(runScheduler);

            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P3"));
            ExpectPipsNotDone(LabelProcess(processes, "P2"));
        }

        /// <summary>
        /// Incremental scheduling does not track the state of the cache so content may
        /// be evicted from the cache and copies will fail since they depend on the content
        /// being present in the cache even though its on disk. This <see>
        ///     <cref>global::BuildXL.Scheduler.Artifacts.FileContentManager</cref>
        /// </see>
        /// should recover the content in this case by storing it into the local cache
        /// </summary>
        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestSchedulingWithContentRecovery(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();
            Dictionary<string, CopyFile> copies = new Dictionary<string, CopyFile>();

            CreateBasicGraph(nodes, files, processes, copies: copies);

            RootFilter filter = CreateFilterForTags(
                new[] {"C1"}.Select(tag => StringId.Create(Context.PathTable.StringTable, tag)),
                new StringId[] {});

            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(filter, testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);

            ExpectPipsDone(LabelPip(copies["C1"], "C1"));

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            // Assume that copy has materialized its output.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["C1"]));

            // Dirty C1 by modifying O4 so that it will be rebuilt
            ModifyFile(files["O4"]);
            runScheduler = await RunScheduler(filter, testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["C1"]));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithDirectoryDependenciesAndOutputDirectories(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, DirectoryArtifact> directories = new Dictionary<string, DirectoryArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateGraphWithDirectoryDependenciesAndOutputs(nodes, files, directories, processes);
            
            bool runScheduler = await RunScheduler();
            XAssert.IsTrue(runScheduler);

            SchedulerTestHooks testHooks;

            ///////////// Scenario 1: Modify source file S2.

            ModifyFile(files["S2"], "Modified S2");

            runScheduler = await RunScheduler(
                testHooks: testHooks = new SchedulerTestHooks
                                       {
                                            IncrementalSchedulingStateAfterJournalScanAction =
                                                state =>
                                                {
                                                    XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
                                                }
                                       });
            XAssert.IsTrue(runScheduler);

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            ExpectPipsDone(LabelProcess(processes, "P2"), LabelProcess(processes, "P3"));

            ExpectPipResults(
                LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P3", PipResultStatus.Succeeded));
            AssertLatestProcessPipCounts(succeeded: 3, hit: 1);

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["S2"]));

            ///////////// Scenario 2: Modify D1/O1.

            ModifyFile(files["D1/O1"], "Modified D1/O1");

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                           {
                               IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                   }
                           });
            XAssert.IsTrue(runScheduler);

            // Every process needs to run.
            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"), LabelProcess(processes, "P3"));

            ExpectPipResults(
                LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P3", PipResultStatus.Succeeded));
            AssertLatestProcessPipCounts(succeeded: 3);

            ///////////// Scenario 3: Remove D1/O2.

            DeleteFile(files["D1/O2"]);

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                           {
                               IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                   }
                           });
            XAssert.IsTrue(runScheduler);

            // Every process needs to run.
            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"), LabelProcess(processes, "P3"));

            ExpectPipResults(
                LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P3", PipResultStatus.Succeeded));
            AssertLatestProcessPipCounts(succeeded: 3);

            ///////////// Scenario 4: Add new file O3 into output directory D1.

            var newFile = CreateFileInDirectory(directories["D1"], "O3");

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                   }
                });
            XAssert.IsTrue(runScheduler);

            // Every process needs to run.
            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"), LabelProcess(processes, "P3"));

            ExpectPipResults(
                LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P3", PipResultStatus.Succeeded));
            AssertLatestProcessPipCounts(succeeded: 3);

            XAssert.IsFalse(File.Exists(newFile), "Added file to the output directory should have been removed");

            ///////////// Scenario 5: Create a file adjacent to D1.
            CreateFileInDirectory(directories["D1"].Path.GetParent(Context.PathTable), "X");

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P3"]));
                                   }
                });
            XAssert.IsTrue(runScheduler);

            // No process needs to run.
            ExpectPipsNotDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"), LabelProcess(processes, "P3"));
            AssertLatestProcessPipCounts(succeeded: 3, hit:3);
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithNonSelectedSealedDirectory(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, DirectoryArtifact> directories = new Dictionary<string, DirectoryArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateGraphWithSealedDirectory(nodes, files, directories, processes);

            // Create filter includes only P1.
            RootFilter onlyP1Filter = CreateFilterForTags(
                new[] {StringId.Create(Context.PathTable.StringTable, "P1")},
                new StringId[0]);

            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(
                filter: onlyP1Filter, 
                testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));

            // P1 executes and consumes SD, thus, although unselected, SD must be marked as clean and materialized.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));

            /////////// Scenario 1: Modify SD/F1
            
            ModifyFile(files["SD/F1"], "Modified SD/F1");

            runScheduler = await RunScheduler(
                filter: onlyP1Filter,
                testHooks: testHooks = new SchedulerTestHooks
                                       {
                                           IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["SD"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["SD"]));
                                               }
                                       });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            ExpectPipsDone(LabelProcess(processes, "P1"));
            ExpectPipResults(LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded));

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));

            // P1 executes and consumes SD, thus, although unselected, SD must be marked as clean and materialized.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));

            /////////// Scenario 2: Modify S3
            
            ModifyFile(files["S3"], "Modified S3");

            runScheduler = await RunScheduler(
                filter: onlyP1Filter,
                testHooks: testHooks = new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["S3"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));
                                               }
                });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            ExpectPipsDone(LabelProcess(processes, "P1"));
            ExpectPipResults(LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded));

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["S3"]));

            // P1 executes and consumes SD, thus, although unselected, SD must be marked as clean and materialized.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithSelectedSealedDirectory(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, DirectoryArtifact> directories = new Dictionary<string, DirectoryArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateGraphWithSealedDirectory(nodes, files, directories, processes);

            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));

            /////////// Scenario 1: Modify SD/F1

            ModifyFile(files["SD/F1"], "Modified SD/F1");

            runScheduler = await RunScheduler(
                testHooks: testHooks = new SchedulerTestHooks
                                       {
                                           IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["SD"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["SD"]));
                                               }
                                       });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            ExpectPipsDone(LabelProcess(processes, "P1"));
            ExpectPipResults(LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded));

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));

            /////////// Scenario 2: Modify S3

            ModifyFile(files["S3"], "Modified S3");

            runScheduler = await RunScheduler(
                testHooks: testHooks = new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));
                                               }
                });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            ExpectPipsDone(LabelProcess(processes, "P1"));
            ExpectPipResults(LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded));

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["S3"]));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithSealedDirectoryButNonConsumedFile(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, DirectoryArtifact> directories = new Dictionary<string, DirectoryArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateGraphWithSealedDirectory(nodes, files, directories, processes);

            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));

            /////////// Scenario 1: Modify SD/F2 -- SD/F2 is not consumed by P1

            ModifyFile(files["SD/F2"], "Modified SD/F2");

            // Because SD/F2 is not consumed by P1, SD/F2 is not tracked, and thus it keeps the clean mark.

            runScheduler = await RunScheduler(
                testHooks: testHooks = new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD/F2"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
                                               }
                });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            ExpectPipsNotDone(LabelProcess(processes, "P1"));

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithSealedDirectoryWhoseMemberIsConsumedDirectly(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, DirectoryArtifact> directories = new Dictionary<string, DirectoryArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            // Create cache for reuse.
            EngineCache cache = InMemoryCacheFactory.Create();

            CreateGraphWithSealedDirectoryWhoseMemberIsConsumedDirectly(nodes, files, directories, processes);

            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(testHooks: testHooks = new SchedulerTestHooks(), cache: cache);
            XAssert.IsTrue(runScheduler);

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));

            /////////// Scenario 1: Modify SD/F2 -- SD/F2 is not consumed by P1, but is consumed directly by P2.

            ModifyFile(files["SD/F2"], "Modified SD/F2");

            // Because SD/F2 is consumed by P2, SD/F2 is tracked, and thus it becomes dirty.
            // Unfortunately P1 becomes dirty as well although it doesn't consume SD/F2.

            runScheduler = await RunScheduler(
                testHooks: testHooks = new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["SD/F2"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["SD"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
                                               }
                },
                cache: cache);
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            // P1 should be up-to-date because it doesn't consume SD/F2.
            ExpectPipResults(LabelProcessWithStatus(processes, "P1", PipResultStatus.UpToDate));
            ExpectPipResults(LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded));

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithSourceSealedDirectory(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, DirectoryArtifact> directories = new Dictionary<string, DirectoryArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateGraphWithSealedSourceDirectory(nodes, files, directories, processes);

            bool runScheduler = await RunScheduler();
            XAssert.IsTrue(runScheduler);

            /////////// Scenario 1: Modify SSD/F2

            ModifyFile(files["SSD/F2"], "Modified SSD/F2");

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                           {
                               IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P3"]));
                                   }
                           });
            XAssert.IsTrue(runScheduler);

            ExpectPipsDone(LabelProcess(processes, "P3"));
            ExpectPipsNotDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"));

            ExpectPipResults(LabelProcessWithStatus(processes, "P3", PipResultStatus.Succeeded));

            /////////// Scenario 2: Modify SSD/F1

            ModifyFile(files["SSD/F1"], "Modified SSD/F1");

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                           {
                               IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P3"]));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
                                   }
                           });
            XAssert.IsTrue(runScheduler);

            ExpectPipsNotDone(LabelProcess(processes, "P3"));
            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"));

            ExpectPipResults(
                LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingSealedSourceAndOutputDirectories(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, DirectoryArtifact> directories = new Dictionary<string, DirectoryArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateGraphWithSealedSourceAndOutputDirectories(nodes, files, directories, processes);

            bool runScheduler = await RunScheduler();
            XAssert.IsTrue(runScheduler);

            /////////// Scenario 1: Modify S1

            ModifyFile(files["S1"], "Modified S1");

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
                                   }
                });
            XAssert.IsTrue(runScheduler);

            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"));
            ExpectPipResults(LabelProcessWithStatus(processes, "P1", PipResultStatus.UpToDate));
            ExpectPipResults(LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded));

            /////////// Scenario 2: Modify SSD/F1

            ModifyFile(files["SSD/F1"], "Modified SSD/F1");

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
                                   }
                });
            XAssert.IsTrue(runScheduler);

            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"));

            ExpectPipResults(
                LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithLazyWriteFile(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, Pip> pips = new Dictionary<string, Pip>();

            CreateGraphForTestingLazyWriteFileMaterialization(pips);

            RootFilter filter = CreateFilterForTags(
                new[] {StringId.Create(Context.PathTable.StringTable, "A")},
                new StringId[0]);

            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(
                filter: filter,
                testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);

            m_configuration.Schedule.EnableLazyOutputMaterialization = true;

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W"].PipId.ToNodeId()));
            XAssert.IsTrue(!iss.DirtyNodeTracker.IsNodeDirty(pips["C1"].PipId.ToNodeId()));
            XAssert.IsTrue(!iss.DirtyNodeTracker.IsNodeMaterialized(pips["C1"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["C2"].PipId.ToNodeId()));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithWriteFilePips(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);
            var pips = new Dictionary<string, Pip>();
            var files = new Dictionary<string, FileArtifact>();

            SchedulerTestHooks testHooks;

            CreateGraphForTestingWriteFile(pips, files);

            bool runScheduler = await RunScheduler();
            XAssert.IsTrue(runScheduler);

            /////////// Scenario 1: Delete WO3

            DeleteFile(files["WO3"]);

            runScheduler = await RunScheduler(
                testHooks: testHooks = new SchedulerTestHooks 
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                   state => 
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(pips["W3"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(pips["P3"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["P1"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["P2"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W1"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W2"].PipId.ToNodeId()));
                                   }
                });
            XAssert.IsTrue(runScheduler);

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W3"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["P3"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["P1"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["P2"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W1"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W2"].PipId.ToNodeId()));

            /////////// Scenario 1: Delete folder X

            FileArtifact xyWO1 = files["X/Y/WO1"];
            AbsolutePath dirX = xyWO1.Path.GetParent(Context.PathTable).GetParent(Context.PathTable);
            DeleteDirectory(dirX, true);

            runScheduler = await RunScheduler(
                testHooks: testHooks = new SchedulerTestHooks 
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                   state => {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W3"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["P3"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(pips["P1"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(pips["P2"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(pips["W1"].PipId.ToNodeId()));
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(pips["W2"].PipId.ToNodeId()));
                                   }
                });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W3"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["P3"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["P1"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["P2"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W1"].PipId.ToNodeId()));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(pips["W2"].PipId.ToNodeId()));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithPipThatEnumeratesNonSealedDirectory(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            NodeId processNode;
            Process process;
            Dictionary<string, AbsolutePath> filesAndDirectories = new Dictionary<string, AbsolutePath>();

            CreateGraphWithPipThatEnumeratesNonSealedDirectory(filesAndDirectories, out processNode, out process);

            bool runScheduler = await RunScheduler();
            XAssert.IsTrue(runScheduler);

            /////////// Scenario 1: Add a new file D/E1/F2.txt

            var dE1F2Txt = FileArtifact.CreateSourceFile(filesAndDirectories["D/E1/F2.txt"]);
            WriteSourceFile(dE1F2Txt);

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                           {
                               IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(processNode));
                                   }
                           });
            XAssert.IsTrue(runScheduler);

            ExpectPipsDone(((Pip) process, "Process"));
            ExpectPipResults(((Pip) process, "Process", PipResultStatus.Succeeded));

            /////////// Scenario 2: Delete an existing file D/E2/F2.txt

            var dE2F2Txt = filesAndDirectories["D/E2/F2.txt"];
            XAssert.IsTrue(FileExists(dE2F2Txt));
            DeleteFile(dE2F2Txt);

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(processNode));
                                   }
                });
            XAssert.IsTrue(runScheduler);

            ExpectPipsDone(((Pip) process, "Process"));
            ExpectPipResults(((Pip) process, "Process", PipResultStatus.Succeeded));

            /////////// Scenario 3: Do nothing

            runScheduler = await RunScheduler(
                testHooks: new SchedulerTestHooks
                           {
                               IncrementalSchedulingStateAfterJournalScanAction =
                                   state =>
                                   {
                                       XAssert.IsFalse(state.DirtyNodeTracker.IsNodeDirty(processNode));
                                   }
                           });
            XAssert.IsTrue(runScheduler);

            ExpectPipsNotDone(((Pip) process, "Process"));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithFilteredLazyOutputs(bool enableGraphAgnostic)
        {
            // Ensure lazy materialization is enabled.
            IncrementalSchedulingSetup(enableGraphAgnosticIncrementalScheduling: enableGraphAgnostic, enableLazyOutputMaterialization: true);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateSimpleGraphWherePipDependsOnTwoProducers(nodes, files, processes);

            // Create filter that excludes P1.
            RootFilter negateP1Filter = CreateFilterForTags(
                new StringId[0],
                new[] {StringId.Create(Context.PathTable.StringTable, "P1")});

            // Create cache for reuse.
            EngineCache cache = InMemoryCacheFactory.Create();

            ///////////// Scenario 1: Clean build.

            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(
                filter: negateP1Filter, 
                cache: cache,
                testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);
            
            // Although filtered out, P1 should be executed because its output O1 is needed by P3, which is filtered in.
            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"), LabelProcess(processes, "P3"));
            ExpectPipResults(
                LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P3", PipResultStatus.Succeeded));

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            // Since P1 executed, P1 is clean and has materialized its output O1.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P3"]));

            XAssert.IsTrue(FileExists(files["O1"]));
            XAssert.IsTrue(FileExists(files["O2"]));
            XAssert.IsTrue(FileExists(files["O3"]));

            ///////////// Scenario 2: Delete output of P1.

            DeleteFile(files["O1"]);

            runScheduler = await RunScheduler(
                filter: negateP1Filter,
                cache: cache,
                testHooks: testHooks = new SchedulerTestHooks
                                       {
                                           IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P3"]));

                                                   // When dirty, P1 should not be marked as materialized.
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["P3"]));
                                               }
                                       });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            // P1 should get cache-hit. But because it's not explicitly selected (by the filter) and due to lazy materialization, P1 does not materialize its output.
            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"), LabelProcess(processes, "P3"));
            ExpectPipResults(
                LabelProcessWithStatus(processes, "P1", PipResultStatus.NotMaterialized),
                LabelProcessWithStatus(processes, "P2", PipResultStatus.UpToDate),
                LabelProcessWithStatus(processes, "P3", PipResultStatus.UpToDate));

            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P3"]));

            // Because P1 does not materialize its output, then that output should not exist.
            XAssert.IsFalse(FileExists(files["O1"]));

            ///////////// Scenario 2: Modify S2.

            ModifyFile(files["S2"], "Modified S2");

            runScheduler = await RunScheduler(
                filter: negateP1Filter,
                cache: cache,
                testHooks: testHooks = new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   // Scanning only marks P2 and P3 dirty. Build set calculator will later mark P1 dirty as well.
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P3"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["P2"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["P3"]));
                                               }
                });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            // Every process needs to run.
            ExpectPipsDone(LabelProcess(processes, "P1"), LabelProcess(processes, "P2"), LabelProcess(processes, "P3"));

            // P1 should get cache-hit, but does not materialize its output. P3 later will deploy that output from cache for its execution.
            ExpectPipResults(
                LabelProcessWithStatus(processes, "P1", PipResultStatus.NotMaterialized),
                LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P3", PipResultStatus.Succeeded));

            // P1 is clean, but has not materialized its outputs.
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P3"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["S2"]));

            // P3 materializes P1's output, but that doesn't mean that P1 has materialized its outputs. P1 can have multiple outputs, but P3 only needs some of them.
            XAssert.IsTrue(FileExists(files["O1"]));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestIncrementalSchedulingWithNonSelectedSealingPipOutput(bool enableGraphAgnostic)
        {
            IncrementalSchedulingSetup(enableGraphAgnostic);

            Dictionary<string, NodeId> nodes = new Dictionary<string, NodeId>();
            Dictionary<string, FileArtifact> files = new Dictionary<string, FileArtifact>();
            Dictionary<string, DirectoryArtifact> directories = new Dictionary<string, DirectoryArtifact>();
            Dictionary<string, Process> processes = new Dictionary<string, Process>();

            CreateGraphWithSealingPipOutput(nodes, files, directories, processes);

            // Create filter includes only P2.
            RootFilter onlyP2Filter = CreateFilterForTags(
                new[] {StringId.Create(Context.PathTable.StringTable, "P2")},
                new StringId[0]);

            // Create cache for reuse.
            EngineCache cache = InMemoryCacheFactory.Create();

            SchedulerTestHooks testHooks;

            bool runScheduler = await RunScheduler(
                filter: onlyP2Filter,
                cache: cache,
                testHooks: testHooks = new SchedulerTestHooks());
            XAssert.IsTrue(runScheduler);

            var iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            ExpectPipsDone(LabelProcess(processes, "P1"));
            ExpectPipsDone(LabelProcess(processes, "P2"));
            ExpectPipResults(LabelProcessWithStatus(processes, "P1", PipResultStatus.Succeeded),
                LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded));

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P1"]));
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));

            // P2 needs to execute, and thus SD should be materialized.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));

            /////////// Scenario 1: Delete O1 and O2, Mofify S2

            DeleteFile(files["O1"]);
            DeleteFile(files["O2"]);
            ModifyFile(files["S2"], "Modified S2");

            runScheduler = await RunScheduler(
                filter: onlyP2Filter,
                cache: cache,
                testHooks: testHooks = new SchedulerTestHooks
                                       {
                                           IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["SD"]));
                                               }
                                       });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            ExpectPipsDone(LabelProcess(processes, "P1"));
            ExpectPipsDone(LabelProcess(processes, "P2"));
            ExpectPipResults(LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded));
            ExpectPipResults(LabelProcessWithStatus(processes, "P1", PipResultStatus.NotMaterialized));

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));

            // P2 needs to execute, and thus SD should be materialized.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));

            // P1 is clean, but not materialized.
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));


            /////////// Scenario 2: Modify S2 again

            ModifyFile(files["S2"], "Modified S2 again");

            runScheduler = await RunScheduler(
                filter: onlyP2Filter,
                cache: cache,
                testHooks: testHooks = new SchedulerTestHooks
                {
                    IncrementalSchedulingStateAfterJournalScanAction =
                                               state =>
                                               {
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeDirty(nodes["P2"]));
                                                   XAssert.IsTrue(state.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
                                                   XAssert.IsFalse(state.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));
                                               }
                });
            XAssert.IsTrue(runScheduler);

            iss = testHooks.IncrementalSchedulingState;
            XAssert.IsNotNull(iss);

            ExpectPipsNotDone(LabelProcess(processes, "P1"));
            ExpectPipsDone(LabelProcess(processes, "P2"));
            ExpectPipResults(LabelProcessWithStatus(processes, "P2", PipResultStatus.Succeeded));

            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["P2"]));

            // P2 needs to execute, and thus SD should be materialized.
            XAssert.IsTrue(iss.DirtyNodeTracker.IsNodeCleanAndMaterialized(nodes["SD"]));

            // P1 is clean, but not materialized.
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeDirty(nodes["P1"]));
            XAssert.IsFalse(iss.DirtyNodeTracker.IsNodeMaterialized(nodes["P1"]));
        }

        private (Pip, string) LabelProcess(IReadOnlyDictionary<string, Process> processes, string label)
        {
            return LabelPip(processes[label], label);
        }

        private (Pip, string, PipResultStatus) LabelProcessWithStatus(
            IReadOnlyDictionary<string, Process> processes,
            string label,
            PipResultStatus status)
        {
            return LabelPipWithStatus(processes[label], label, status);
        }
    }
}
