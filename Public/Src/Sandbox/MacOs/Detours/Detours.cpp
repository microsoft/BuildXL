// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Detours.hpp"
#include "MemoryStreams.hpp"
#include "Trie.hpp"

static std::once_flag InitializeWritePathCache;
static std::once_flag InitializeSocket;
static std::once_flag RetrySocketInitialization;

static int socket_handle = -1;

#pragma mark Utility Functions

inline std::string get_executable_path(pid_t pid)
{
    char fullpath[PATH_MAX] = { '\0' };
    int success = proc_pidpath(pid, (void *) fullpath, PATH_MAX);
    return success > 0 ? std::string(fullpath) : std::string("/unknown-process");
}

inline void setup_socket()
{
    if ((socket_handle = socket(AF_UNIX, SOCK_STREAM, 0)) == -1)
    {
        log_debug("%s", "Socket creation failed, aborting because conistent sandboxing can't be guaranteed!");
        abort();
    }

    struct sockaddr_un socket_addr;
    memset(&socket_addr, 0, sizeof(socket_addr));
    socket_addr.sun_family = AF_UNIX;
    strncpy(socket_addr.sun_path, socket_path, sizeof(socket_addr.sun_path) - 1);

    int result = connect(socket_handle, (struct sockaddr*)&socket_addr, sizeof(socket_addr));
    if (result < 0)
    {
        log_debug("%s", "Connecting to socket failed, aborting because conistent sandboxing can't be guaranteed!");
        abort();
    }
}

inline void send_to_sandbox(IOEvent &event, es_event_type_t type = ES_EVENT_TYPE_LAST)
{
    if (event.IsPlistEvent() || event.IsDirectorySpecialCharacterEvent())
    {
        return;
    }
    
    std::call_once(InitializeSocket, []()
    {
        setup_socket();
    });
    
    size_t msg_length = IOEvent::max_size();
    char msg[msg_length];
    memset(msg, '\0', msg_length);
    
    omemorystream oms(msg, sizeof(msg));
    oms << event;
    
    uint retries = 0;
    size_t total_bytes_written = 0;
    
    // Always send full sized messages although the actual event can be shorter, currently we do this
    // to avoid implementing complex package chunking logic on the build host
    while (total_bytes_written < msg_length)
    {
        ssize_t written = send(socket_handle, msg + total_bytes_written, msg_length - total_bytes_written, 0);
        if (written < 0)
        {
            switch (errno)
            {
                case EBADF:
                case EBUSY:
                case ENFILE:
                case EMFILE:
                case EAGAIN: {
                    std::call_once(RetrySocketInitialization, [&]()
                    {
                        setup_socket();
                        log_debug("Observation message (%{public}s) could not be transmitted retrying socket setup...", msg);
                    });
                    
                    continue;
                }
            }
            
            if (retries > 100)
            {
                log_debug("Observation message could not be transmitted after several retries, aborting because conistent sandboxing can't be guaranteed - error: %d", errno);
                abort();
            }
            
            retries++;
            continue;
        }
        else if (written == 0)
        {
            log_debug("%s", "Connection reset by host, aborting because conistent sandboxing can't be guaranteed!");
            abort();
        }
        else
        {
            total_bytes_written += written;
        }
    }
    log_debug("Successfully sent: %{public}.*s", (int)msg_length, msg);
    assert(total_bytes_written == msg_length);
}

#pragma mark Interposing Notes

/*
    Endpoint Security Events not (yet) mapped:

    ES_EVENT_TYPE_NOTIFY_STAT
    ES_EVENT_TYPE_NOTIFY_CHROOT
    ES_EVENT_TYPE_NOTIFY_LOOKUP
    ES_EVENT_TYPE_NOTIFY_READDIR
    ES_EVENT_TYPE_NOTIFY_DUP
    ES_EVENT_TYPE_NOTIFY_SETACL
 
    Posix / BSD Notes:
 
    Most of the interposed methods have file descriptor equivalents that are not covered (yet)
*/

#pragma mark Spawn / Fork Family Functions

int bxl_posix_spawn(pid_t *child_pid,
    const char *path,
    const posix_spawn_file_actions_t *file_actions,
    const posix_spawnattr_t *attrp,
    char *const *argv,
    char *const *envp)
{
    pid_t inject = 0;
    if (child_pid == NULL) child_pid = &inject;

    pid_t pid = getpid();
    pid_t ppid = getppid();
    int result = posix_spawn(child_pid, path, file_actions, attrp, argv, envp);
    FORK_EVENT_CONSTRUCTOR(result, child_pid, pid, ppid, ==)
}
DYLD_INTERPOSE(bxl_posix_spawn, posix_spawn)

int bxl_posix_spawnp(pid_t *child_pid,
    const char *file,
    const posix_spawn_file_actions_t *file_actions,
    const posix_spawnattr_t *attrp,
    char *const *argv,
    char *const *envp)
{
    pid_t inject = 0;
    if (child_pid == NULL) child_pid = &inject;
    
    pid_t pid = getpid();
    pid_t ppid = getppid();
    int result = posix_spawnp(child_pid, file, file_actions, attrp, argv, envp);
    FORK_EVENT_CONSTRUCTOR(result, child_pid, pid, ppid, ==)
}
DYLD_INTERPOSE(bxl_posix_spawnp, posix_spawnp)

pid_t blx_fork(void)
{
    pid_t result = fork();
    FORK_EVENT_CONSTRUCTOR(result, &result, getpid(), getppid(), >)
}
DYLD_INTERPOSE(blx_fork, fork)

pid_t blx_vfork(void)
{
    pid_t result = vfork();
    FORK_EVENT_CONSTRUCTOR(result, &result, getpid(), getppid(), >)
}
DYLD_INTERPOSE(blx_vfork, vfork)

#pragma mark Exec Family Functions

// execve() ist the backend to all other exec family calls, interposing here is enough
int bxl_execve(const char *path, char *const argv[], char *const envp[])
{
    // Sending the event has to happen prior to the execve call as it only ever returns on error
    EXEC_EVENT_CONSTRUCTOR(path)
    return execve(path, argv, envp);
}
DYLD_INTERPOSE(bxl_execve, execve)

#pragma mark Exit Functions

void bxl_exit(int s)
{
    std::string fullpath = get_executable_path(getpid());
    IOEvent event(getpid(), 0, getppid(), ES_EVENT_TYPE_NOTIFY_EXIT, "", "", fullpath, false);
    send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_EXIT);

    exit(s);
}
DYLD_INTERPOSE(bxl_exit, exit)

void bxl__exit(int s)
{
    std::string fullpath = get_executable_path(getpid());
    IOEvent event(getpid(), 0, getppid(), ES_EVENT_TYPE_NOTIFY_EXIT, "", "", fullpath, false);
    send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_EXIT);

    _exit(s);
}
DYLD_INTERPOSE(bxl__exit, _exit)

void bxl__Exit(int s)
{
    std::string fullpath = get_executable_path(getpid());
    IOEvent event(getpid(), 0, getppid(), ES_EVENT_TYPE_NOTIFY_EXIT, "", "", fullpath, false);
    send_to_sandbox(event, ES_EVENT_TYPE_NOTIFY_EXIT);

    _Exit(s);
}
DYLD_INTERPOSE(bxl__Exit, _Exit)

#pragma mark Open / Close Family Functions

int bxl_open(const char *path, int oflag)
{
    int result = open(path, oflag);
    int old_errno = errno;

    es_event_type_t type = ES_EVENT_TYPE_NOTIFY_OPEN;
    if ((oflag & O_CREAT) == O_CREAT) type = ES_EVENT_TYPE_NOTIFY_CREATE;
    else if ((oflag & O_TRUNC) == O_TRUNC) type = ES_EVENT_TYPE_NOTIFY_TRUNCATE;

    IOEvent event(getpid(), 0, getppid(), type, path, "", get_executable_path(getpid()));
    send_to_sandbox(event);

    errno = old_errno;
    return result;
}
DYLD_INTERPOSE(bxl_open, open)

int bxl_close(int fildes)
{
    char path[PATH_MAX] = { '\0' };
    int success = fcntl(fildes, F_GETPATH, path);

    int result = close(fildes);
    int old_errno = errno;

    if (result == 0 && success == 0)
    {
        // TODO: Explore ways to infer if the closed file was actually modified e.g. open() path/handle cache then lookup + mod time check on close?
        IOEvent event(getpid(), 0, getppid(), ES_EVENT_TYPE_NOTIFY_CLOSE, path, "", get_executable_path(getpid()));
        send_to_sandbox(event);
    }
    errno = old_errno;
    return result;
}
DYLD_INTERPOSE(bxl_close, close)

#pragma mark Symlink Family Functions

size_t bxl_readlink(const char* path, char* buf, size_t bufsize)
{
    size_t result = readlink(path, buf, bufsize);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_READLINK, path, "", true)
}
DYLD_INTERPOSE(bxl_readlink, readlink)

int bxl_link(const char *src, const char *dst)
{
    int result = link(src, dst);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_LINK, src, dst, true)
}
DYLD_INTERPOSE(bxl_link, link)

int bxl_symlink(const char *path1, const char *path2)
{
    int result = symlink(path1, path2);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_LINK, path1, path2, true)
}
DYLD_INTERPOSE(bxl_symlink, symlink)

int bxl_unlink(const char *path)
{
    int result = unlink(path);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_UNLINK, path, "", true)
}
DYLD_INTERPOSE(bxl_unlink, unlink)

#pragma mark Attribute / Extended Attribute Family Functions

int bxl_getattrlist(const char* path, struct attrlist* attrList, void* attrBuf, size_t attrBufSize, unsigned int options)
{
    int result = getattrlist(path, attrList, attrBuf, attrBufSize, options);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_GETATTRLIST, path, "", true)
}
DYLD_INTERPOSE(bxl_getattrlist, getattrlist)

ssize_t bxl_getxattr(const char *path, const char *name, void *value, size_t size, u_int32_t position, int options)
{
    ssize_t result = getxattr(path, name, value, size, position, options);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_GETEXTATTR, path, "", true)
}
DYLD_INTERPOSE(bxl_getxattr, getxattr)

ssize_t bxl_listxattr(const char *path, char *namebuff, size_t size, int options)
{
    ssize_t result = listxattr(path, namebuff, size, options);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_LISTEXTATTR, path, "", true)
}
DYLD_INTERPOSE(bxl_listxattr, listxattr)

int bxl_setattrlist(const char* path, struct attrlist* attrList, void* attrBuf, size_t attrBufSize, unsigned int options)
{
    int result = setattrlist(path, attrList, attrBuf, attrBufSize, options);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_SETATTRLIST, path, "", true)
}
DYLD_INTERPOSE(bxl_setattrlist, setattrlist)

int bxl_setxattr(const char *path, const char *name, const void *value, size_t size, u_int32_t position, int options)
{
    int result = setxattr(path, name, value, size, position, options);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_SETEXTATTR, path, "", true)
}
DYLD_INTERPOSE(bxl_setxattr, setxattr)

int bxl_removexattr(const char *path, const char *name, int options)
{
    int result = removexattr(path, name, options);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR, path, "", true)
}
DYLD_INTERPOSE(bxl_removexattr, removexattr)

#pragma mark ACL Family Functions

int bxl_chflags(const char *path, __uint32_t flags)
{
    int result = chflags(path, flags);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_SETFLAGS, path, "", true)
}
DYLD_INTERPOSE(bxl_chflags, chflags)

int bxl_chmod(const char *path, mode_t mode)
{
    int result = chmod(path, mode);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_SETMODE, path, "", true)
}
DYLD_INTERPOSE(bxl_chmod, chmod)

int bxl_chown(const char *path, uid_t owner, gid_t group)
{
    int result = chown(path, owner, group);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_SETOWNER, path, "", true)
}
DYLD_INTERPOSE(bxl_chown, chown)

int bxl_access(const char *path, int mode)
{
    int result = access(path, mode);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_ACCESS, path, "", true)
}
DYLD_INTERPOSE(bxl_access, access)

acl_t bxl_acl_get_file(const char *path_p, acl_type_t type)
{
    acl_t result = acl_get_file(path_p, type);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_ACCESS, path_p, "", true)
}
DYLD_INTERPOSE(acl_get_file, acl_get_file)

acl_t bxl_acl_get_link_np(const char *path_p, acl_type_t type)
{
    acl_t result = acl_get_link_np(path_p, type);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_ACCESS, path_p, "", true)
}
DYLD_INTERPOSE(bxl_acl_get_link_np, acl_get_link_np)

#pragma mark Rename / Exchange / Clone / Truncate Family Functions

int bxl_rename(const char *src, const char *dst)
{
    int result = rename(src, dst);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_RENAME, src, dst, false)
}
DYLD_INTERPOSE(bxl_rename, rename)

int bxl_exchangedata(const char * path1, const char * path2, unsigned int options)
{
    int result = exchangedata(path1, path2, options);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA, path1, path2, false)
}
DYLD_INTERPOSE(bxl_exchangedata, exchangedata)

int bxl_clonefile(const char *src, const char *dst, int flags)
{
    int result = clonefile(src, dst, flags);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_CLONE, src, dst, false)
}
DYLD_INTERPOSE(bxl_clonefile, clonefile)

int bxl_truncate(const char *path, off_t length)
{
    int result = truncate(path, length);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_TRUNCATE, path, "", true)
}
DYLD_INTERPOSE(bxl_truncate, truncate)

#pragma mark Generic I/O Functions

ssize_t bxl_fsgetpath(char *restrict_buf, size_t buflen, fsid_t *fsid, uint64_t obj_id)
{
    ssize_t result = fsgetpath(restrict_buf, buflen, fsid, obj_id);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_FSGETPATH, restrict_buf, "", true)
}
DYLD_INTERPOSE(bxl_fsgetpath, fsgetpath)

int bxl_utimes(const char *path, const struct timeval times[2])
{
    int result = utimes(path, times);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_UTIMES, path, "", true)
}
DYLD_INTERPOSE(bxl_utimes, utimes)

int bxl_chdir(const char *path)
{
    int result = chdir(path);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_CHDIR, path, "", true)
}
DYLD_INTERPOSE(bxl_chdir, chdir)

#pragma mark Write Family Functions + Caching

static Trie<PathCacheEntry> *trackedPaths_;

ssize_t bxl_pwrite(int fildes, const void *buf, size_t nbyte, off_t offset)
{
    std::call_once(InitializeWritePathCache, []()
    {
        trackedPaths_ = Trie<PathCacheEntry>::createPathTrie();
    });

    char path[PATH_MAX] = { '\0' };
    int success = fcntl(fildes, F_GETPATH, path);
    ssize_t result = pwrite(fildes, buf, nbyte, offset);
    WRITE_EVENT_CONSTRUCTOR(path, "")
}
DYLD_INTERPOSE(bxl_pwrite, pwrite)

ssize_t bxl_write(int fildes, const void *buf, size_t nbyte)
{
    std::call_once(InitializeWritePathCache, []()
    {
        trackedPaths_ = Trie<PathCacheEntry>::createPathTrie();
    });

    char path[PATH_MAX] = { '\0' };
    int success = fcntl(fildes, F_GETPATH, path);
    ssize_t result = write(fildes, buf, nbyte);
    WRITE_EVENT_CONSTRUCTOR(path, "")
}
DYLD_INTERPOSE(bxl_write, write)
