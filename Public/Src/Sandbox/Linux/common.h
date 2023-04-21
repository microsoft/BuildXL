// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef COMMON_H
#define COMMON_H

// CODESYNC: Public/Src/Engine/Processes/SandboxConnectionLinuxDetours.cs
#define BxlEnvFamPath "__BUILDXL_FAM_PATH"
#define BxlEnvRootPid "__BUILDXL_ROOT_PID"
#define BxlEnvDetoursPath "__BUILDXL_DETOURS_PATH"
#define BxlPTraceMqName "__BUILDXL_PTRACE_MQ_NAME"
#define BxlPTraceRunnerPath "__BUILDXL_PTRACE_RUNNER_PATH"
#define BxlPTraceForcedProcessNames "__BUILDXL_PTRACE_FORCED_PROCESSES"

// This value was picked to be PATH_MAX * 2, this should be enough to communicate the FAM and exe path between the ptrace daemon and runner
#define PTRACED_MQ_MSG_SIZE 8192

enum ptracecommand
{
    run,
    exitnotification
};

#endif //COMMON_H
