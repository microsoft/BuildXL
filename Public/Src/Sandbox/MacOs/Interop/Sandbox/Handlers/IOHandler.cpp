// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "IOHandler.hpp"

#pragma mark Process life cycle

void IOHandler::HandleProcessFork(const es_message_t *msg)
{
    // don't track if child processes are allowed to break away
    if (GetPip()->AllowChildProcessesToBreakAway())
    {
        return;
    }
    
    es_event_fork_t fork = msg->event.fork;
    pid_t childPorcessPid = audit_token_to_pid(fork.child->audit_token);
    
    if (GetSandbox()->TrackChildProcess(childPorcessPid, GetProcess()))
    {
        ReportChildProcessSpawned(childPorcessPid);
    }
}

void IOHandler::HandleProcessExec(const es_message_t *msg)
{
    es_event_exec_t exec = msg->event.exec;
    PathInfo info = PathInfo(exec.target->executable);
    GetProcess()->SetPath(info.Path());
    
    // report child process to clients only (tracking happens on 'fork's not 'exec's)
    ReportChildProcessSpawned(GetProcess()->GetPid());
}

void IOHandler::HandleProcessExit(const es_message_t *msg)
{
    id_t pid = audit_token_to_pid(msg->process->audit_token);
    
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

void IOHandler::HandleLookup(const es_message_t *msg)
{
    es_event_lookup_t lookup = msg->event.lookup;
    PathInfo lookupInfo = PathInfo(lookup.source_dir, lookup.relative_target);
    CheckAndReport(kOpMacLookup, lookupInfo.Path(), Checkers::CheckLookup, msg, false);
}

void IOHandler::HandleOpen(const es_message_t *msg)
{
    es_event_open_t open = msg->event.open;
    PathInfo info = PathInfo(open.file);
    
    bool isDir = S_ISDIR(info.Stat().st_mode);
    CheckFunc checker = isDir ? Checkers::CheckEnumerateDir : Checkers::CheckRead;
    FileOperation op  = isDir ? kOpKAuthOpenDir : kOpKAuthReadFile;
        
    CheckAndReport(op, info.Path(), checker, msg, isDir);
}

void IOHandler::HandleClose(const es_message_t *msg)
{
    es_event_close_t close = msg->event.close;
    PathInfo info = PathInfo(close.target);
    
    if (close.modified)
    {
        CheckAndReport(kOpKAuthCloseModified, info.Path(), Checkers::CheckWrite, msg);
        return;
    }
    
    /*
        TODO: This is currently the same code as in HandleOpen. HandleOpen is not hooked up due to the symmetry between
              open <-> close and to reduce the number of processed callbacks from ES
     */

    bool isDir = S_ISDIR(info.Stat().st_mode);

    CheckFunc checker = isDir ? Checkers::CheckEnumerateDir : Checkers::CheckRead;
    FileOperation op  = isDir ? kOpKAuthOpenDir : kOpKAuthReadFile;
    
    CheckAndReport(op, info.Path(), checker, msg, isDir);
        
    // TODO: Should this be reported?
    // CheckAndReport(kOpKAuthClose, info.Path(), Checkers::CheckRead, msg);
}

void IOHandler::HandleLink(const es_message_t *msg)
{
    es_event_link_t link = msg->event.link;

    PathInfo sourceInfo = PathInfo(link.source);
    PathInfo destInfo = PathInfo(link.target_dir, link.target_filename);
    
    CheckAndReport(kOpKAuthCreateHardlinkSource, sourceInfo.Path(), Checkers::CheckRead, msg);
    CheckAndReport(kOpKAuthCreateHardlinkDest, destInfo.Path(), Checkers::CheckWrite, msg);
}

void IOHandler::HandleUnlink(const es_message_t *msg)
{
    es_event_unlink_t unlink = msg->event.unlink;
    bool isDir = S_ISDIR(unlink.target->stat.st_mode);
    FileOperation operation = isDir ? kOpKAuthDeleteDir : kOpKAuthDeleteFile;
    
    PathInfo info = PathInfo(unlink.target);
    CheckAndReport(operation, info.Path(), Checkers::CheckWrite, msg);
}

void IOHandler::HandleReadlink(const es_message_t *msg)
{
    es_event_readlink_t readlink = msg->event.readlink;
    
    PathInfo info = PathInfo(readlink.source);
    AccessCheckResult checkResult = CheckAndReport(kOpMacReadlink, info.Path(), Checkers::CheckRead, msg, false);
    
    if (checkResult.ShouldDenyAccess())
    {
        // TODO: Deny access?
    }
}

void IOHandler::HandleRename(const es_message_t *msg)
{
    es_event_rename_t rename = msg->event.rename;
    PathInfo srcInfo = PathInfo(rename.source);
    CheckAndReport(kOpKAuthMoveSource, srcInfo.Path(), Checkers::CheckRead, msg);
    
    switch (rename.destination_type)
    {
        case ES_DESTINATION_TYPE_EXISTING_FILE:
        {
            PathInfo dstInfo = PathInfo(rename.destination.existing_file);
            CheckAndReport(kOpKAuthMoveDest, dstInfo.Path(), Checkers::CheckWrite, msg);
            break;
        }
        case ES_DESTINATION_TYPE_NEW_PATH:
        {
            PathInfo dstInfo = PathInfo(rename.destination.new_path.dir);
            CheckAndReport(kOpKAuthMoveDest, dstInfo.Path(), Checkers::CheckWrite, msg);
            break;
        }
    }
}

void IOHandler::HandleClone(const es_message_t *msg)
{
    es_event_clone_t clone = msg->event.clone;
    
    PathInfo f1Info = PathInfo(clone.source);
    PathInfo f2Info = PathInfo(clone.target_dir, clone.target_name);
    
    CheckAndReport(kOpMacVNodeCloneSource, f1Info.Path(), Checkers::CheckReadWrite, msg);
    CheckAndReport(kOpMacVNodeCloneDest, f2Info.Path(), Checkers::CheckReadWrite, msg);
}

void IOHandler::HandleExchange(const es_message_t *msg)
{
    es_event_exchangedata_t exchange = msg->event.exchangedata;
        
    PathInfo f1Info = PathInfo(exchange.file1);
    PathInfo f2Info = PathInfo(exchange.file2);

    CheckAndReport(kOpKAuthCopySource, f1Info.Path(), Checkers::CheckReadWrite, msg);
    CheckAndReport(kOpKAuthCopyDest, f2Info.Path(), Checkers::CheckReadWrite, msg);
}

void IOHandler::HandleCreate(const es_message_t *msg)
{
    es_event_create_t create = msg->event.create;
    bool existingFile = create.destination_type == ES_DESTINATION_TYPE_EXISTING_FILE;
    
    PathInfo info = existingFile
        ? PathInfo(create.destination.existing_file)
        : PathInfo(create.destination.new_path.dir);

    CheckFunc checker = Checkers::CheckWrite;
    bool isDir = false;
    
    if (existingFile)
    {
        mode_t mode = info.Stat().st_mode;
        bool enforceDirectoryCreation = CheckDirectoryCreationAccessEnforcement(GetFamFlags());
        
        isDir = S_ISDIR(mode);
        
        checker =
            S_ISLNK(mode) ? Checkers::CheckCreateSymlink
                          : S_ISREG(mode)
                            ? Checkers::CheckWrite
                            : enforceDirectoryCreation
                                ? Checkers::CheckCreateDirectory
                                : Checkers::CheckProbe;
    }

    AccessCheckResult result = CheckAndReport(kOpMacVNodeCreate, info.Path(), checker, msg, isDir);
    
    if (result.ShouldDenyAccess())
    {
        // TODO: Deny access?
    }
}

void IOHandler::HandleGenericWrite(const es_message_t *msg)
{
    const char *path = nullptr;
    mode_t mode = 0;

    switch (msg->event_type) {
        case ES_EVENT_TYPE_NOTIFY_SETATTRLIST:
        {
            es_event_setattrlist_t setattrlist = msg->event.setattrlist;
            PathInfo info = PathInfo(setattrlist.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_SETEXTATTR:
        {
            es_event_setextattr_t setinfoattr = msg->event.setextattr;
            PathInfo info = PathInfo(setinfoattr.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_SETFLAGS:
        {
            es_event_setflags_t flags = msg->event.setflags;
            PathInfo info = PathInfo(flags.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_SETMODE:
        {
            es_event_setmode_t setmode = msg->event.setmode;
            PathInfo info = PathInfo(setmode.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_SETACL:
        {
            es_event_setacl_t setacl = msg->event.setacl;
            PathInfo info = PathInfo(setacl.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR:
        {
            es_event_deleteextattr_t deleteinfoattr = msg->event.deleteextattr;
            PathInfo info = PathInfo(deleteinfoattr.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_TRUNCATE:
        {
            es_event_truncate_t truncate = msg->event.truncate;
            PathInfo info = PathInfo(truncate.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_WRITE:
        {
            es_event_write_t write = msg->event.write;
            PathInfo info = PathInfo(write.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        default:
        {
            log_error("Failed to map HandleGenericWrite to EndpointSecurity event (%d)!", msg->event_type);
            break;
        }
    }
    
    if (path != nullptr)
    {
        CheckAndReport(kOpMacVNodeWrite, path, Checkers::CheckWrite, msg, S_ISDIR(mode));
    }
}

void IOHandler::HandleGenericRead(const es_message_t *msg)
{
    const char *path = nullptr;
    mode_t mode = 0;
    
    switch (msg->event_type) {
        case ES_EVENT_TYPE_NOTIFY_GETATTRLIST:
        {
            es_event_getattrlist_t getattrlist = msg->event.getattrlist;
            PathInfo info = PathInfo(getattrlist.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_GETEXTATTR:
        {
            es_event_getextattr_t getinfoattr = msg->event.getextattr;
            PathInfo info = PathInfo(getinfoattr.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_LISTEXTATTR:
        {
            es_event_listextattr_t listinfoattr = msg->event.listextattr;
            PathInfo info = PathInfo(listinfoattr.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_ACCESS:
        {
            es_event_access_t access = msg->event.access;
            PathInfo info = PathInfo(access.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_STAT:
        {
            es_event_stat_t stat = msg->event.stat;
            PathInfo info = PathInfo(stat.target);
            path = info.Path();
            mode = info.Stat().st_mode;
            break;
        }
        default:
        {
            log_error("Failed to map HandleGenericRead to EndpointSecurity event (%d)!", msg->event_type);
            break;
        }
    }

    if (path != nullptr)
    {
        CheckAndReport(kOpKAuthVNodeRead, path, Checkers::CheckRead, msg, S_ISDIR(mode));
    }
}
