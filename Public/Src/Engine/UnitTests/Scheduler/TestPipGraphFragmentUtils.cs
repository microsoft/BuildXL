// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Process = BuildXL.Pips.Operations.Process;
using ProcessOutputs = BuildXL.Pips.Builders.ProcessOutputs;

namespace Test.BuildXL.Scheduler
{
    public static class TestPipGraphFragmentUtils
    {
        public static (IIpcMoniker ipcMoniker, PipId servicePipId) CreateService(TestPipGraphFragment fragment, ServiceRelatedPips pips = null)
        {
            var ipcMoniker = fragment.GetIpcMoniker();
            var apiServerMoniker = fragment.GetApiServerMoniker();

            var shutdownBuilder = fragment.GetProcessBuilder();
            new ArgumentsBuilder(shutdownBuilder)
                .AddIpcMonikerOption("--ipcMoniker ", ipcMoniker)
                .AddIpcMonikerOption("--serverMoniker ", apiServerMoniker)
                .AddOutputFileOption("--output ", fragment.CreateOutputFile("shutdown.txt"))
                .Finish();
            shutdownBuilder.ServiceKind = ServicePipKind.ServiceShutdown;
            (Process shutdownProcess, ProcessOutputs _) = fragment.ScheduleProcessBuilder(shutdownBuilder);

            var finalProcessBuilder = fragment.GetIpcProcessBuilder();
            new ArgumentsBuilder(finalProcessBuilder)
                .AddStringOption("--command ", "final")
                .AddIpcMonikerOption("--ipcMoniker ", ipcMoniker)
                .Finish();
            var finalOutputFile = fragment.CreateOutputFile("final.txt");
            var finalizationPip = fragment.ScheduleIpcPip(
                ipcMoniker,
                null,
                finalProcessBuilder,
                finalOutputFile,
                true);
            XAssert.IsTrue(finalizationPip.PipId.IsValid);

            var serviceProcessBuilder = fragment.GetProcessBuilder();
            new ArgumentsBuilder(serviceProcessBuilder)
                .AddIpcMonikerOption("--ipcMoniker ", ipcMoniker)
                .AddIpcMonikerOption("--serverMoniker ", apiServerMoniker)
                .AddOutputFileOption("--output ", fragment.CreateOutputFile("service.txt"))
                .Finish();
            serviceProcessBuilder.ServiceKind = ServicePipKind.Service;
            serviceProcessBuilder.ShutDownProcessPipId = shutdownProcess.PipId;
            serviceProcessBuilder.FinalizationPipIds = ReadOnlyArray<PipId>.FromWithoutCopy(new[] { finalizationPip.PipId });
            (Process serviceProcess, ProcessOutputs _) = fragment.ScheduleProcessBuilder(serviceProcessBuilder);

            var createProcessBuilder = fragment.GetIpcProcessBuilder();
            new ArgumentsBuilder(createProcessBuilder)
                .AddStringOption("--command ", "create")
                .AddIpcMonikerOption("--ipcMoniker ", ipcMoniker)
                .Finish();
            var createOutputFile = fragment.CreateOutputFile("create.txt");
            var createPip = fragment.ScheduleIpcPip(
                ipcMoniker,
                serviceProcess.PipId,
                createProcessBuilder,
                createOutputFile,
                false);
            XAssert.IsTrue(createPip.PipId.IsValid);

            if (pips != null)
            {
                pips.ShutDown = shutdownProcess;
                pips.Final = finalizationPip;
                pips.ServiceStart = serviceProcess;
                pips.Create = createPip;
            }

            return (ipcMoniker, serviceProcess.PipId);
        }

        public class ServiceRelatedPips
        {
            public Pip ShutDown;
            public Pip Final;
            public Pip ServiceStart;
            public Pip Create;
        }
    }
}
