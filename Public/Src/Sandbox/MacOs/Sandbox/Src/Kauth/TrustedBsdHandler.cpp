// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "TrustedBsdHandler.hpp"
#include "OpNames.hpp"

int TrustedBsdHandler::HandleLookup(const char *path)
{
    // set last looked up path
    Stopwatch stopwatch;
    GetPip()->setLastLookedUpPath(path);

    Timespan duration = stopwatch.lap();
    GetSandbox()->Counters()->setLastLookedUpPath += duration;
    GetPip()->Counters()->setLastLookedUpPath += duration;

    // Check, report, but never deny lookups
    CheckAndReport(kOpMacLookup, path, Checkers::CheckLookup, /*isDir*/ false);
    return KERN_SUCCESS;
}

int TrustedBsdHandler::HandleReadVnode(vnode_t vnode, FileOperation operationToReport, bool isVnodeDir)
{
    // get symlink path
    char path[MAXPATHLEN];
    int len = MAXPATHLEN;
    int err = vn_getpath(vnode, path, &len);
    if (err)
    {
        log_error("Could not get VNnode path for %d operation; error code: %#X", operationToReport, err);
        return KERN_SUCCESS; // don't deny access because of our own error
    }

    // check read access
    AccessCheckResult checkResult = CheckAndReport(operationToReport, path, Checkers::CheckRead, isVnodeDir);
    
    if (checkResult.ShouldDenyAccess())
    {
        LogAccessDenied(path, operationToReport, "Operation: Read Vnode");
        return EPERM;
    }
    else
    {
        return KERN_SUCCESS;
    }
}

int TrustedBsdHandler::HandleVNodeCreateEvent(const char *fullPath,
                                              const bool isDir,
                                              const bool isSymlink)
{
    bool enforceDirectoryCreation = CheckDirectoryCreationAccessEnforcement(GetFamFlags());
    CheckFunc checker =
        isSymlink                          ? Checkers::CheckCreateSymlink :
        !isDir                             ? Checkers::CheckWrite :
        enforceDirectoryCreation           ? Checkers::CheckCreateDirectory :
                                             Checkers::CheckProbe;
    AccessCheckResult result = CheckAndReport(kOpMacVNodeCreate, fullPath, checker, isDir);

    if (result.ShouldDenyAccess())
    {
        LogAccessDenied(fullPath, 0, "Operation: VNodeCreate");
        return EPERM;
    }
    else
    {
        return KERN_SUCCESS;
    }
}

int TrustedBsdHandler::HandleVnodeWrite(vnode_t vnode, FileOperation operation)
{
    char path[MAXPATHLEN];
    int len = MAXPATHLEN;
    int err = vn_getpath(vnode, path, &len);
    if (err)
    {
        log_error("Could not get VNnode path for write operation; error code: %#X", err);
        return KERN_SUCCESS; // don't deny access because of our own error
    }

    return HandleWritePath(path, operation);
}

int TrustedBsdHandler::HandleWritePath(const char *path, FileOperation operation)
{
    // check write access
    AccessCheckResult checkResult = CheckAndReport(operation, path, Checkers::CheckWrite, /*isDir*/ false);
    if (checkResult.ShouldDenyAccess())
    {
        LogAccessDenied(path, 0, "Operation: Write");
        return EPERM;
    }
    else
    {
        return KERN_SUCCESS;
    }
}

// TODO: We could take advantage of knowing what's on critical path, and not slow down those processes
//       This information could be conveyed via the FileAccessManifest
void TrustedBsdHandler::HandleProcessWantsToFork(const pid_t parentProcessPid)
{
    // Only throttle when the root process wants to fork
    // TODO: this should be configurable via FAM
    if (parentProcessPid == GetProcessId())
    {
        GetSandbox()->ResourceManger()->WaitForCpu();
    }
}

void TrustedBsdHandler::HandleProcessFork(const pid_t childProcessPid)
{
    if (GetSandbox()->TrackChildProcess(childProcessPid, GetProcess()))
    {
        ReportChildProcessSpawned(childProcessPid);
        GetPip()->Counters()->numForks++;
    }
}

void TrustedBsdHandler::HandleProcessExec(const vnode_t vp)
{
    // get the full path to 'vp' and save it to process->processName_
    int len = MAXPATHLEN;
    vn_getpath(vp, GetProcess()->getPathBuffer(), &len);

    // report child process to clients only (tracking happens on 'fork's not 'exec's)
    ReportChildProcessSpawned(GetProcess()->getPid());
}

void TrustedBsdHandler::HandleProcessExit(const pid_t pid)
{
    ReportProcessExited(pid);
    HandleProcessUntracked(pid);
}

void TrustedBsdHandler::HandleProcessUntracked(const pid_t pid)
{
    GetSandbox()->UntrackProcess(pid, GetProcess());
    if (GetPip()->getTreeSize() == 0)
    {
        ReportProcessTreeCompleted();
    }
}
