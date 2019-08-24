// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if ES_SANDBOX

#include <assert.h>
#include <sys/types.h>
#include <sys/stat.h>
#include <unistd.h>

#include "IOHandler.hpp"
#include "OpNames.hpp"

class PathExtractor
{
private:
    
    typedef struct { char data[PATH_MAX]; size_t length; } Buffer;
    Buffer buffer_;
    
public:
    
    PathExtractor(const char *buffer, size_t length)
    {
        assert(length <= PATH_MAX);
        char *end = (char *) memccpy(buffer_.data, buffer, '\0', length);
        if (end == NULL) buffer_.data[length] = '\0';
        buffer_.length = strlen(buffer_.data);
    }

    ~PathExtractor() { }

    inline const char *Path() { return buffer_.data; }
    inline const size_t PathLength() { return buffer_.length; }
};

static mode_t get_mode(const char *path, int *error)
{
    assert(error != NULL);
    
    struct stat path_stat;
    // Do not follow symlinks
    *error = lstat(path, &path_stat);
    return path_stat.st_mode;
}

static bool exists(const char *path)
{
    int error = NO_ERROR;
    get_mode(path, &error);
    return error == NO_ERROR;
}

#pragma mark Process life cycle

void IOHandler::HandleProcessFork(const es_message_t *msg)
{
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
    PathExtractor ext = PathExtractor(exec.target->executable->path.data, exec.target->executable->path.length);
    GetProcess()->setPath(ext.Path());
    
    // report child process to clients only (tracking happens on 'fork's not 'exec's)
    ReportChildProcessSpawned(GetProcess()->getPid());
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
    if (GetPip()->getTreeSize() == 0)
    {
        ReportProcessTreeCompleted(pid);
    }
}

#pragma mark Process I/O observation

void IOHandler::HandleLookup(const es_message_t *msg)
{
    es_event_lookup_t lookup = msg->event.lookup;
    
    PathExtractor srcExt = PathExtractor(lookup.source_dir->path.data, lookup.source_dir->path.length);
    PathExtractor relExt = PathExtractor(lookup.relative_target.data, lookup.relative_target.length);
    
    char path[PATH_MAX];
    size_t length = srcExt.PathLength() + relExt.PathLength() + 1;
    assert(length < PATH_MAX);
    
    int index = snprintf(path, PATH_MAX, "%.*s%s%.*s",
                         (int) srcExt.PathLength(), srcExt.Path(),
                         // Handle the case where source dir has no trailing /
                         (srcExt.PathLength() == 1) ? "" : "/",
                         (int) relExt.PathLength(), relExt.Path());
    
    path[index] = '\0';
    
    CheckAndReport(kOpMacLookup, path, Checkers::CheckLookup, msg, false);
    
    // TODO: KAuth offered notifications for file attribute, extended attribute and security flag reading, those are emitted once for every
    //       file system entry also used by a process, we emit the probe here blindly when the lookup path exits and until we get
    //       appropriate ES events implemented
    int error = NO_ERROR;
    mode_t mode = get_mode(path, &error);
    if (error == NO_ERROR)
    {
        bool isDir = !S_ISREG(mode);
        CheckAndReport(kOpKAuthVNodeProbe, path, Checkers::CheckProbe, msg, isDir);
    }
}

void IOHandler::HandleOpen(const es_message_t *msg)
{
    es_event_open_t open = msg->event.open;
    PathExtractor ext = PathExtractor(open.file->path.data, open.file->path.length);
    
    int error = NO_ERROR;
    mode_t mode = get_mode(ext.Path(), &error);
    
    if (error == NO_ERROR)
    {
        bool isDir = !S_ISREG(mode);
        
        CheckFunc checker = isDir ? Checkers::CheckEnumerateDir : Checkers::CheckRead;
        FileOperation op  = isDir ? kOpKAuthOpenDir : kOpKAuthReadFile;
        
        CheckAndReport(op, ext.Path(), checker, msg, isDir);
        return;
    }
    
    log_error("Failed to report HandleOpen: Error %d\n", error);
}

void IOHandler::HandleClose(const es_message_t *msg)
{
    es_event_close_t close = msg->event.close;
    PathExtractor ext = PathExtractor(close.target->path.data, close.target->path.length);
    
    if (close.modified)
    {
        CheckAndReport(kOpKAuthCloseModified, ext.Path(), Checkers::CheckWrite, msg);
        return;
    }
    
    // TODO: This is currently the same code as in HandleOpen. HandleOpen is not hooked up due to the symmetry between
    //       open <-> close and to reduce the number of processed callbacks from ES
    int error = NO_ERROR;
    mode_t mode = get_mode(ext.Path(), &error);
    
    if (error == NO_ERROR)
    {
        bool isDir = !S_ISREG(mode);
        
        CheckFunc checker = isDir ? Checkers::CheckEnumerateDir : Checkers::CheckRead;
        FileOperation op  = isDir ? kOpKAuthOpenDir : kOpKAuthReadFile;
        
        CheckAndReport(op, ext.Path(), checker, msg, isDir);
        
        // TODO: Should this be reported?
        // CheckAndReport(kOpKAuthClose, close.target->path.data, Checkers::CheckRead, msg);
        return;
    }
    
    log_error("Failed to report HandleClose: Error %d\n", error);
}

void IOHandler::HandleLink(const es_message_t *msg)
{
    es_event_link_t link = msg->event.link;
    
    PathExtractor dirExt = PathExtractor(link.target_dir->path.data, link.target_dir->path.length);
    PathExtractor fileExt = PathExtractor(link.target_filename.data, link.target_filename.length);
    
    char path[PATH_MAX];
    size_t length = dirExt.PathLength() + fileExt.PathLength() + 1;
    assert(length < PATH_MAX);
    
    int index = snprintf(path, PATH_MAX, "%.*s%.*s", (int) strlen(dirExt.Path()), dirExt.Path(), (int) fileExt.PathLength(), fileExt.Path());
    path[index] = '\0';
    
    CheckAndReport(kOpKAuthCreateHardlinkSource, link.source->path.data, Checkers::CheckRead, msg);
    CheckAndReport(kOpKAuthCreateHardlinkDest, path, Checkers::CheckWrite, msg);
}

void IOHandler::HandleUnlink(const es_message_t *msg)
{
    es_event_unlink_t unlink = msg->event.unlink;
    int error = NO_ERROR;
    mode_t mode = get_mode(unlink.target->path.data, &error);
    
    if (error == NO_ERROR)
    {
        bool isDir = !S_ISREG(mode);
        
        FileOperation operation = isDir ? kOpKAuthDeleteDir : kOpKAuthDeleteFile;
        
        PathExtractor ext = PathExtractor(unlink.target->path.data, unlink.target->path.length);
        CheckAndReport(operation, ext.Path(), Checkers::CheckWrite, msg);
        return;
    }
    
    log_error("Failed to report HandleUnlink: Error %d\n", error);
}

void IOHandler::HandleReadlink(const es_message_t *msg)
{
    es_event_readlink_t readlink = msg->event.readlink;
    
    PathExtractor ext = PathExtractor(readlink.source->path.data, readlink.source->path.length);
    AccessCheckResult checkResult = CheckAndReport(kOpMacReadlink, ext.Path(), Checkers::CheckRead, msg, false);
    
    if (checkResult.ShouldDenyAccess())
    {
        // TODO: Deny access
    }
}

void IOHandler::HandleRename(const es_message_t *msg)
{
    es_event_rename_t rename = msg->event.rename;
    PathExtractor srcExt = PathExtractor(rename.source->path.data, rename.source->path.length);
    CheckAndReport(kOpKAuthMoveSource, srcExt.Path(), Checkers::CheckRead, msg);
    
    switch (rename.destination_type)
    {
        case ES_DESTINATION_TYPE_EXISTING_FILE:
        {
            PathExtractor dstExt = PathExtractor(rename.destination.existing_file->path.data, rename.destination.existing_file->path.length);
            CheckAndReport(kOpKAuthMoveDest, dstExt.Path(), Checkers::CheckWrite, msg);
            break;
        }
        case ES_DESTINATION_TYPE_NEW_PATH:
        {
            PathExtractor dstExt = PathExtractor(rename.destination.new_path.dir->path.data, rename.destination.new_path.dir->path.length);
            CheckAndReport(kOpKAuthMoveDest, dstExt.Path(), Checkers::CheckWrite, msg);
            break;
        }
    }
}

void IOHandler::HandleExchange(const es_message_t *msg)
{
    es_event_exchangedata_t exchange = msg->event.exchangedata;
    
    PathExtractor f1Ext = PathExtractor(exchange.file1->path.data, exchange.file1->path.length);
    PathExtractor f2Ext = PathExtractor(exchange.file2->path.data, exchange.file2->path.length);
    
    CheckAndReport(kOpKAuthCopySource, f1Ext.Path(), Checkers::CheckReadWrite, msg);
    CheckAndReport(kOpKAuthCopyDest, f2Ext.Path(), Checkers::CheckReadWrite, msg);
}

void IOHandler::HandleCreate(const es_message_t *msg)
{
    es_event_create_t create = msg->event.create;
    
    PathExtractor ext = PathExtractor(create.target->path.data, create.target->path.length);
    int error = NO_ERROR;
    
    if (error == NO_ERROR)
    {
        mode_t mode = get_mode(ext.Path(), &error);
        
        bool enforceDirectoryCreation = CheckDirectoryCreationAccessEnforcement(GetFamFlags());
        CheckFunc checker =
            S_ISLNK(mode) ? Checkers::CheckCreateSymlink :
            S_ISREG(mode) ? Checkers::CheckWrite :
            enforceDirectoryCreation ? Checkers::CheckCreateDirectory :
                                       Checkers::CheckProbe;
        
        AccessCheckResult result = CheckAndReport(kOpMacVNodeCreate, ext.Path(), checker, msg, !S_ISREG(mode));

        if (result.ShouldDenyAccess())
        {
            // TODO: Deny access
        }
        
        return;
    }
        
    log_error("Failed to report HandleCreate: Error %d\n", error);
}

void IOHandler::HandleWrite(const es_message_t *msg)
{
    const char *path = nullptr;
    
    switch (msg->event_type) {
        case ES_EVENT_TYPE_NOTIFY_SETATTRLIST:
        {
            es_event_setattrlist_t setattrlist = msg->event.setattrlist;
            PathExtractor ext = PathExtractor(setattrlist.target->path.data, setattrlist.target->path.length);
            path = ext.Path();
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_SETEXTATTR:
        {
            es_event_setextattr_t setextattr = msg->event.setextattr;
            PathExtractor ext = PathExtractor(setextattr.target->path.data, setextattr.target->path.length);
            path = ext.Path();
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_SETFLAGS:
        {
            es_event_setflags_t flags = msg->event.setflags;
            PathExtractor ext = PathExtractor(flags.target->path.data, flags.target->path.length);
            path = ext.Path();
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_SETMODE:
        {
            es_event_setmode_t setmode = msg->event.setmode;
            PathExtractor ext = PathExtractor(setmode.target->path.data, setmode.target->path.length);
            path = ext.Path();
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_WRITE:
        {
            es_event_write_t write = msg->event.write;
            PathExtractor ext = PathExtractor(write.target->path.data, write.target->path.length);
            path = ext.Path();
            break;
        }
        default:
        {
            log_error("Failed to map HandleWrite to EndpointSecurity event (%d)!", msg->event_type);
            break;
        }
    }
    
    if (path != nullptr) CheckAndReport(kOpKAuthVNodeWrite, path, Checkers::CheckWrite, msg);
}

#endif /* ES_SANDBOX */
