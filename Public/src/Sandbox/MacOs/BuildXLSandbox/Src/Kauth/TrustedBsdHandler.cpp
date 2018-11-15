//
//  TrustedBsdHandler.cpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "TrustedBsdHandler.hpp"
#include "OpNames.hpp"

int TrustedBsdHandler::HandleLookup(const char *path)
{
    // remember last looked up path
    const OSSymbol *lookupPathSymbol = OSSymbol::withCString(path);
    SetLastLookedUpPath(lookupPathSymbol);
    OSSafeReleaseNULL(lookupPathSymbol);
    
    // Check, report, but never deny lookups
    CheckAndReport(kOpMacLookup, path, Checkers::CheckReadNonexistent);
    return KERN_SUCCESS;
}

int TrustedBsdHandler::HandleReadlink(vnode_t symlinkVNode)
{
    // get symlink path
    char path[MAXPATHLEN];
    int len = MAXPATHLEN;
    int err = vn_getpath(symlinkVNode, path, &len);
    if (err)
    {
        log_error("Could not get VNnode path for readlink operation; error code: %#X", err);
        return KERN_SUCCESS; // don't deny access because of our own error
    }
    
    // check read access
    AccessCheckResult checkResult = CheckAndReport(kOpMacReadlink, path, Checkers::CheckRead);
    
    if (checkResult.ShouldDenyAccess())
    {
        LogAccessDenied(path, 0, "Operation: Readlink");
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
                                             Checkers::CheckRead;
    AccessCheckResult result = CheckAndReport(kOpMacVNodeCreate, fullPath, checker);

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

void TrustedBsdHandler::HandleProcessFork(const pid_t childProcessPid)
{
    if (GetSandbox()->TrackChildProcess(childProcessPid, GetProcess()))
    {
        char procName[MAXPATHLEN] = {0};
        proc_name(childProcessPid, procName, sizeof(procName));
        ReportChildProcessSpawned(childProcessPid, procName);
    }
}

void TrustedBsdHandler::HandleProcessExit(const pid_t pid)
{
    ReportProcessExited(pid);
    HandleProcessUntracked(pid);
}

void TrustedBsdHandler::HandleProcessUntracked(const pid_t pid)
{
    ProcessObject *process = GetProcess();
    GetSandbox()->UntrackProcess(pid, process);
    if (process->hasEmptyProcessTree())
    {
        ReportProcessTreeCompleted();
    }
}
