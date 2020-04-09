// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "IOHandler.hpp"

#pragma mark Process life cycle

void IOHandler::HandleProcessFork(const IOEvent &event)
{
    if (GetPip()->AllowChildProcessesToBreakAway())
    {
        return;
    }

    pid_t childProcessPid = event.GetChildPid();
    if (GetSandbox()->TrackChildProcess(childProcessPid, event.GetExecutablePath(), GetProcess()))
    {
        ReportChildProcessSpawned(childProcessPid);
    }
}

void IOHandler::HandleProcessExec(const IOEvent &event)
{
    GetProcess()->SetPath(event.GetExecutablePath());
    ReportChildProcessSpawned(GetProcess()->GetPid());
}

void IOHandler::HandleProcessExit(const IOEvent &event)
{
    pid_t pid = event.GetPid();
    
    ReportProcessExited(pid);
    HandleProcessUntracked(pid);
}

void IOHandler::HandleProcessUntracked(const pid_t pid)
{
    GetSandbox()->UntrackProcess(pid, GetProcess());
    if (GetPip()->GetTreeSize() == 0)
    {
        ReportProcessTreeCompleted(GetPip()->GetProcessId());
    }
}

#pragma mark Process I/O observation

void IOHandler::HandleLookup(const IOEvent &event)
{
    CheckAndReport(kOpMacLookup, event.GetEventPath(SRC_PATH), Checkers::CheckLookup, event.GetPid(), /*isDir*/ false);
}

void IOHandler::HandleOpen(const IOEvent &event)
{
    bool isDir = S_ISDIR(event.GetMode());
    
    // When interposing, we get every open() call attempt regardless of success, open calls on
    // non-existent paths are currently treated as lookups.
    if (!event.EventPathExists())
    {
        CheckAndReport(kOpMacLookup, event.GetEventPath(SRC_PATH), Checkers::CheckLookup, event.GetPid(), isDir);
        return;
    }
    
    CheckFunc checker = isDir ? Checkers::CheckEnumerateDir : Checkers::CheckRead;
    FileOperation op  = isDir ? kOpKAuthOpenDir : kOpKAuthReadFile;
        
    CheckAndReport(op, event.GetEventPath(SRC_PATH), checker, event.GetPid(), isDir);
}

void IOHandler::HandleClose(const IOEvent &event)
{
    if (event.FSEntryModified())
    {
        CheckAndReport(kOpKAuthCloseModified, event.GetEventPath(SRC_PATH), Checkers::CheckWrite, event.GetPid());
        return;
    }
        
    bool isDir = S_ISDIR(event.GetMode());
    CheckAndReport(kOpKAuthClose, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid(), isDir);
}

void IOHandler::HandleLink(const IOEvent &event)
{
    CheckAndReport(kOpKAuthCreateHardlinkSource, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid());
    CheckAndReport(kOpKAuthCreateHardlinkDest, event.GetEventPath(DST_PATH), Checkers::CheckWrite, event.GetPid());
}

void IOHandler::HandleUnlink(const IOEvent &event)
{
    bool isDir = S_ISDIR(event.GetMode());
    FileOperation operation = isDir ? kOpKAuthDeleteDir : kOpKAuthDeleteFile;
    
    CheckAndReport(operation, event.GetEventPath(SRC_PATH), Checkers::CheckWrite, event.GetPid());
}

void IOHandler::HandleReadlink(const IOEvent &event)
{
    AccessCheckResult checkResult = CheckAndReport(kOpMacReadlink, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid(), false);
    
    if (checkResult.ShouldDenyAccess())
    {
        // TODO: Deny access?
    }
}

void IOHandler::HandleRename(const IOEvent &event)
{
    CheckAndReport(kOpKAuthMoveSource, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid());
    CheckAndReport(kOpKAuthMoveDest, event.GetEventPath(DST_PATH), Checkers::CheckWrite, event.GetPid());
}

void IOHandler::HandleClone(const IOEvent &event)
{
    CheckAndReport(kOpMacVNodeCloneSource, event.GetEventPath(SRC_PATH), Checkers::CheckReadWrite, event.GetPid());
    CheckAndReport(kOpMacVNodeCloneDest, event.GetEventPath(DST_PATH), Checkers::CheckReadWrite, event.GetPid());
}

void IOHandler::HandleExchange(const IOEvent &event)
{
    CheckAndReport(kOpKAuthCopySource, event.GetEventPath(SRC_PATH), Checkers::CheckReadWrite, event.GetPid());
    CheckAndReport(kOpKAuthCopyDest, event.GetEventPath(DST_PATH), Checkers::CheckReadWrite, event.GetPid());
}

void IOHandler::HandleCreate(const IOEvent &event)
{
    CheckFunc checker = Checkers::CheckWrite;
    bool isDir = false;
    
    if (event.EventPathExists())
    {
        mode_t mode = event.GetMode();
        bool enforceDirectoryCreation = CheckDirectoryCreationAccessEnforcement(GetFamFlags());
        isDir = S_ISDIR(mode);
        
        checker =
            S_ISLNK(mode) ? Checkers::CheckCreateSymlink
                          : S_ISREG(mode)
                            ? Checkers::CheckWrite
                            : enforceDirectoryCreation
                                ? Checkers::CheckCreateDirectory
                                : Checkers::CheckCreateDirectoryNoEnforcement;
    }
    
    AccessCheckResult result = CheckAndReport(isDir ? kOpKAuthCreateDir : kOpMacVNodeCreate, event.GetEventPath(SRC_PATH), checker, event.GetPid(), isDir);

    if (result.ShouldDenyAccess())
    {
        // TODO: Deny access?
    }
}

void IOHandler::HandleGenericWrite(const IOEvent &event)
{
    const char *path = event.GetEventPath(SRC_PATH);
    mode_t mode = event.GetMode();
    bool isDir = S_ISDIR(mode);
    
    CheckAndReport(kOpKAuthVNodeWrite, path, Checkers::CheckWrite, event.GetPid(), isDir);
}

void IOHandler::HandleGenericRead(const IOEvent &event)
{
    const char *path = event.GetEventPath(SRC_PATH);
    mode_t mode = event.GetMode();
    bool isDir = S_ISDIR(mode);
    
    if (!event.EventPathExists())
    {
        CheckAndReport(kOpMacLookup, path, Checkers::CheckLookup, event.GetPid(), false);
    }
    else
    {
        CheckAndReport(kOpKAuthVNodeRead, path, Checkers::CheckRead, event.GetPid(), isDir);
    }
}

void IOHandler::HandleGenericProbe(const IOEvent &event)
{
    const char *path = event.GetEventPath(SRC_PATH);
    mode_t mode = event.GetMode();
    bool isDir = S_ISDIR(mode);

    if (!event.EventPathExists())
    {
        CheckAndReport(kOpMacLookup, path, Checkers::CheckLookup, event.GetPid(), false);
    }
    else
    {
        CheckAndReport(kOpKAuthVNodeProbe, path, Checkers::CheckProbe, event.GetPid(), isDir);
    }
}

void IOHandler::HandleEvent(const IOEvent &event)
{
    switch (event.GetEventType())
    {
        case ES_EVENT_TYPE_NOTIFY_EXEC:
            return HandleProcessExec(event);

        case ES_EVENT_TYPE_NOTIFY_FORK:
            return HandleProcessFork(event);

        case ES_EVENT_TYPE_NOTIFY_EXIT:
            return HandleProcessExit(event);

        case ES_EVENT_TYPE_NOTIFY_LOOKUP:
            return HandleLookup(event);

        case ES_EVENT_TYPE_NOTIFY_OPEN:
            return HandleOpen(event);

        case ES_EVENT_TYPE_NOTIFY_CLOSE:
            return HandleClose(event);

        case ES_EVENT_TYPE_NOTIFY_CREATE:
            return HandleCreate(event);

        case ES_EVENT_TYPE_NOTIFY_TRUNCATE:
        case ES_EVENT_TYPE_NOTIFY_SETATTRLIST:
        case ES_EVENT_TYPE_NOTIFY_SETEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_SETFLAGS:
        case ES_EVENT_TYPE_NOTIFY_SETOWNER:
        case ES_EVENT_TYPE_NOTIFY_SETMODE:
        case ES_EVENT_TYPE_NOTIFY_WRITE:
        case ES_EVENT_TYPE_NOTIFY_UTIMES:
        case ES_EVENT_TYPE_NOTIFY_SETTIME:
        case ES_EVENT_TYPE_NOTIFY_SETACL:
            return HandleGenericWrite(event);

        case ES_EVENT_TYPE_NOTIFY_READDIR:
        case ES_EVENT_TYPE_NOTIFY_FSGETPATH:
            return HandleGenericRead(event);

        case ES_EVENT_TYPE_NOTIFY_GETATTRLIST:
        case ES_EVENT_TYPE_NOTIFY_GETEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_LISTEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_ACCESS:
        case ES_EVENT_TYPE_NOTIFY_STAT:
            return HandleGenericProbe(event);

        case ES_EVENT_TYPE_NOTIFY_CLONE:
            return HandleClone(event);

        case ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA:
            return HandleExchange(event);

        case ES_EVENT_TYPE_NOTIFY_RENAME:
            return HandleRename(event);

        case ES_EVENT_TYPE_NOTIFY_READLINK:
            return HandleReadlink(event);

        case ES_EVENT_TYPE_NOTIFY_LINK:
            return HandleLink(event);

        case ES_EVENT_TYPE_NOTIFY_UNLINK:
            return HandleUnlink(event);

        default:
            std::string message("Unhandled ES event: ");
            message.append(std::to_string(event.GetEventType()));
            throw BuildXLException(message);
    }
}
