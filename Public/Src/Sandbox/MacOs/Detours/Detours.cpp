// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Detours.hpp"
#include "MemoryStreams.hpp"
#include "PathCacheEntry.hpp"
#include "Trie.hpp"
#include "XPCConstants.hpp"

#pragma mark Static state

static std::once_flag InitializeOpenPathCache;
static std::once_flag InitializeWritePathCache;
static std::once_flag InitializeXPC;

static xpc_connection_t bxl_connection = nullptr;
static thread_local bool bxl_realpath_execution = false;

#pragma mark Utility Functions

char *bxl_realpath(const char* file_name, char* buffer)
{
    bxl_realpath_execution = true;
    char *result = realpath(file_name, buffer);
    bxl_realpath_execution = false;
    return result;
}

int setup_xpc()
{
    char queue_name[PATH_MAX] = { '\0' };
    sprintf(queue_name, "com.microsoft.buildxl.detours.proc_%d_%u", getpid(), arc4random_uniform(1024^2));

     dispatch_queue_t xpc_queue = dispatch_queue_create(queue_name, dispatch_queue_attr_make_with_qos_class(
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INTERACTIVE, -1
    ));

    xpc_connection_t xpc_connection = xpc_connection_create_mach_service("com.microsoft.buildxl.sandbox", NULL, 0);
    xpc_connection_set_event_handler(xpc_connection, ^(xpc_object_t message)
    {
        xpc_type_t type = xpc_get_type(message);
        if (type == XPC_TYPE_ERROR)
        {
            const char *desc = xpc_copy_description(message);
            fprintf(stderr, "Connecting to XPC bridge service failed, aborting because conistent sandboxing can't be guaranteed: %s\n", desc);
            abort();
        }
    });

    xpc_connection_resume(xpc_connection);
    
    xpc_object_t xpc_payload = xpc_dictionary_create(NULL, NULL, 0);
    xpc_dictionary_set_uint64(xpc_payload, "command", xpc_get_detours_connection);
    xpc_object_t response = xpc_connection_send_message_with_reply_sync(xpc_connection, xpc_payload);

    xpc_type_t type = xpc_get_type(response);
    uint64_t status = xpc_response_error;
    
    if (type == XPC_TYPE_DICTIONARY)
    {
        status = xpc_dictionary_get_uint64(response, "response");
        if (status == xpc_response_success)
        {
            xpc_endpoint_t endpoint = (xpc_endpoint_t) xpc_dictionary_get_value(response, "connection");
            bxl_connection = xpc_connection_create_from_endpoint(endpoint);
            xpc_connection_set_event_handler(bxl_connection, ^(xpc_object_t message)
            {
                xpc_type_t type = xpc_get_type(message);
                if (type == XPC_TYPE_ERROR)
                {
                    const char *desc = xpc_copy_description(message);
                    fprintf(stderr, "Connecting to XPC bridge service failed, aborting because conistent sandboxing can't be guaranteed: %s\n", desc);
                    abort();
                }
            });

            xpc_connection_set_target_queue(bxl_connection, xpc_queue);
            xpc_connection_resume(bxl_connection);
            xpc_connection_suspend(xpc_connection);
        }
        else
        {
            const char *desc = xpc_copy_description(response);
            fprintf(stderr, "Error from XPC response: %s\n", desc);
        }
    }
    else
    {
        const char *desc = xpc_copy_description(response);
        fprintf(stderr, "Error parsing connection response payload: %s\n", desc);
    }

    xpc_release(response);
    return bxl_connection != nullptr ? EXIT_SUCCESS : EXIT_FAILURE;
}

inline void handle_xpc_setup()
{
    if (setup_xpc() == EXIT_FAILURE)
    {
        // Abort, sandboxing can't be enabled
        abort();
    }
}

inline void send_to_sandbox(IOEvent &event, es_event_type_t type = ES_EVENT_TYPE_LAST, bool force_xpc_init = false, bool resolve_paths = true)
{
    if (event.IsPlistEvent() || event.IsDirectorySpecialCharacterEvent())
    {
        return;
    }

    std::call_once(InitializeXPC, []()
    {
        handle_xpc_setup();
    });

    // Some interposed syscalls invalidate XPC sessions, re-initialize when required
    if (force_xpc_init)
    {
        handle_xpc_setup();
    }

    if (resolve_paths)
    {
        char src_resolved[PATH_MAX + 1] = { '\0' };
        bxl_realpath(event.GetEventPath(SRC_PATH), src_resolved);
        event.SetEventPath(src_resolved, SRC_PATH);

        char dst_resolved[PATH_MAX + 1] = { '\0' };
        bxl_realpath(event.GetEventPath(DST_PATH), dst_resolved);
        event.SetEventPath(dst_resolved, DST_PATH);
    }

    size_t msg_length = IOEvent::max_size();
    char msg[msg_length];

    omemorystream oms(msg, sizeof(msg));
    oms << event;

    xpc_object_t xpc_payload = xpc_dictionary_create(NULL, NULL, 0);
    xpc_dictionary_set_string(xpc_payload, IOEventKey, msg);
    xpc_dictionary_set_uint64(xpc_payload, IOEventLengthKey, event.Size());

    xpc_object_t response = xpc_connection_send_message_with_reply_sync(bxl_connection, xpc_payload);
    xpc_type_t xpc_type = xpc_get_type(response);

    uint64_t status = xpc_response_error;
    if (xpc_type == XPC_TYPE_DICTIONARY)
    {
        status = xpc_dictionary_get_uint64(response, "response");
    }

    xpc_release(response);

    if (status != xpc_response_success)
    {
        fprintf(stderr, "Connecting to XPC bridge service failed, aborting because conistent sandboxing can't be guaranteed - status(%lld)\n", status);
        abort();
    }
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

#pragma mark Path Utilities

inline std::string get_executable_path(pid_t pid)
{
    char fullpath[PATH_MAX] = { '\0' };
    int success = proc_pidpath(pid, (void *) fullpath, PATH_MAX);
    return success > 0 ? fullpath : "/unknown-process";
}

#pragma mark Spawn / Fork Family Functions

extern char** environ;

char* get_env_interposing_entry(char *const *env)
{
    uint count = 0;
    while(environ[count])
    {
        if(strstr(environ[count], "libBuildXLDetours") != NULL)
        {
            char *value = (char *) calloc(strlen(environ[count]) + 1, sizeof(char));
            strncpy(value, environ[count], strlen(environ[count]));
            return value;
        }

        count++;
    }

    return nullptr;
}

char** extend_env_with_interposing_lib(char *const *env, char* interpose)
{
    if (interpose == nullptr) return (char **)env;

    uint count = 0;
    while (env[count]) count++;

    char **new_env = (char **) malloc(sizeof(char *) * (count + 2));

    count = 0;
    while (env[count])
    {
        new_env[count] = (char *) calloc(strlen(env[count]) + 1, sizeof(char));
        memcpy(new_env[count], env[count], strlen(env[count]));
        count++;
    }

    new_env[count] = (char *) calloc(strlen(interpose) + 1, sizeof(char));
    memcpy(new_env[count], interpose, strlen(interpose));
    new_env[++count] = NULL;
    free(interpose);

    return new_env;
}

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

    char *interpose = get_env_interposing_entry(envp);
    char **new_env = extend_env_with_interposing_lib(envp, interpose);

    int result = posix_spawn(child_pid, path, file_actions, attrp, argv, new_env);
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

    char *interpose = get_env_interposing_entry(envp);
    char **new_env = extend_env_with_interposing_lib(envp, interpose);

    int result = posix_spawnp(child_pid, file, file_actions, attrp, argv, new_env);
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
    char *interpose = get_env_interposing_entry(envp);
    char **new_env = extend_env_with_interposing_lib(envp, interpose);

    return execve(path, argv, new_env);
}
DYLD_INTERPOSE(bxl_execve, execve)

#pragma mark Exit Functions

void bxl_exit(int s)
{
    EXIT_EVENT_CONSTRUCTOR()
    exit(s);
}
DYLD_INTERPOSE(bxl_exit, exit)

void bxl__exit(int s)
{
   EXIT_EVENT_CONSTRUCTOR()
    _exit(s);
}
DYLD_INTERPOSE(bxl__exit, _exit)

void bxl__Exit(int s)
{
    EXIT_EVENT_CONSTRUCTOR()
    _Exit(s);
}
DYLD_INTERPOSE(bxl__Exit, _Exit)

void __attribute__ ((constructor)) _bxl_linux_sandbox_init(void)
{
    atexit_b(^()
    {
        EXIT_EVENT_CONSTRUCTOR()
    });
}

#pragma mark Open / Close Family Functions

// This cache is used to mitigate heavy enumeration operations from tools like clang, e.g. when searching header include paths
static Trie<PathCacheEntry> *openedPaths_;

int bxl_open(const char *path, int oflag)
{
    std::call_once(InitializeOpenPathCache, []()
    {
        openedPaths_ = Trie<PathCacheEntry>::createPathTrie();
    });

    int result = open(path, oflag);
    int old_errno = errno;

    bool reported = openedPaths_->get(path) != nullptr;
    if (!reported)
    {
        es_event_type_t type = ES_EVENT_TYPE_NOTIFY_OPEN;
        if ((oflag & (O_CREAT | O_TRUNC | O_WRONLY)) == O_CREAT) type = ES_EVENT_TYPE_NOTIFY_CREATE;

        std::shared_ptr<PathCacheEntry> entry(new PathCacheEntry(path, 0));
        openedPaths_->insert(path, entry);
        IOEvent event(getpid(), 0, getppid(), type, ES_ACTION_TYPE_NOTIFY, path, "", get_executable_path(getpid()), true);
        send_to_sandbox(event);
    }

    errno = old_errno;
    return result;
}
DYLD_INTERPOSE(bxl_open, open)

/*

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

 */

#pragma mark Symlink Family Functions

size_t bxl_readlink(const char* path, char* buf, size_t bufsize)
{
    size_t result = readlink(path, buf, bufsize);
    DEFAULT_EVENT_CONSTRUCTOR_NO_RESOLVE(ES_EVENT_TYPE_NOTIFY_READLINK, path, "", true, true /* Always report readlinks */)
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
    DEFAULT_EVENT_CONSTRUCTOR_NO_RESOLVE(ES_EVENT_TYPE_NOTIFY_CREATE, path1, path2, true, true)
}
DYLD_INTERPOSE(bxl_symlink, symlink)

int bxl_unlink(const char *path)
{
    int result = unlink(path);
    DEFAULT_EVENT_CONSTRUCTOR_NO_RESOLVE(ES_EVENT_TYPE_NOTIFY_UNLINK, path, "", true, true)
}
DYLD_INTERPOSE(bxl_unlink, unlink)

#pragma mark Attribute / Extended Attribute Family Functions

int bxl_getattrlist(const char* path, struct attrlist* attrList, void* attrBuf, size_t attrBufSize, unsigned int options)
{
    int result = getattrlist(path, attrList, attrBuf, attrBufSize, options);
    DEFAULT_EVENT_CONSTRUCTOR_NO_RESOLVE(ES_EVENT_TYPE_NOTIFY_GETATTRLIST, path, "", true, !bxl_realpath_execution)
}
DYLD_INTERPOSE(bxl_getattrlist, getattrlist)

ssize_t bxl_getxattr(const char *path, const char *name, void *value, size_t size, u_int32_t position, int options)
{
    ssize_t result = getxattr(path, name, value, size, position, options);
    DEFAULT_EVENT_CONSTRUCTOR_NO_RESOLVE(ES_EVENT_TYPE_NOTIFY_GETEXTATTR, path, "", true, !bxl_realpath_execution)
}
DYLD_INTERPOSE(bxl_getxattr, getxattr)

ssize_t bxl_listxattr(const char *path, char *namebuff, size_t size, int options)
{
    ssize_t result = listxattr(path, namebuff, size, options);
    DEFAULT_EVENT_CONSTRUCTOR_NO_RESOLVE(ES_EVENT_TYPE_NOTIFY_LISTEXTATTR, path, "", true, !bxl_realpath_execution)
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

#pragma mark File System Utility Functions

int bxl_mkdir(const char *path, mode_t mode)
{
    int result = mkdir(path, mode);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_CREATE, path, "", true)
}
DYLD_INTERPOSE(bxl_mkdir, mkdir)

int bxl_creat(const char *path, mode_t mode)
{
    int result = creat(path, mode);
    DEFAULT_EVENT_CONSTRUCTOR(ES_EVENT_TYPE_NOTIFY_CREATE, path, "", true)
}
DYLD_INTERPOSE(bxl_creat, creat)
