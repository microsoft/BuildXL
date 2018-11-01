//
//  Listeners.cpp
//  BuildXLSandbox
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "Listeners.hpp"
#include "FileOpHandler.hpp"
#include "ProcessObject.hpp"
#include "TrustedBsdHandler.hpp"
#include "VNodeHandler.hpp"

int RELEVANT_KAUTH_VNODE_BITS =
    KAUTH_VNODE_GENERIC_READ_BITS |
    KAUTH_VNODE_GENERIC_WRITE_BITS |
    KAUTH_VNODE_GENERIC_EXECUTE_BITS;

#pragma mark Scope FileOperation Callbacks

void *Listeners::g_dispatcher = nullptr;

static int ComputeAbsolutePath(struct vnode *vp, const char *const relPath, size_t relPathLen, char *resultBuf, int resultBufLen)
{
    assert(vp != nullptr);
    assert(relPath != nullptr);
    assert(relPathLen >= 0);
    assert(resultBuf != nullptr);
    assert(resultBufLen > 0);

    // compute full path by getting the absolute path of 'vp' and appending the relative path 'relPath'
    int len = resultBufLen;
    int err = 0;
    if ((err = vn_getpath(vp, resultBuf, &len)) != 0)
    {
        return err;
    }

    if (relPathLen > 0)
    {
        // if the path returned by vn_getpath ends with '/' don't append another '/'
        // (this only happens when dvp corresponds to the root folder)
        // (NOTE: 'len' is the length of the buffer, which is the same as strlen(resultBuf) + 1)
        if (len >= 2 && resultBuf[len - 2] == '/')
        {
            len -= 1;
        }

        // check if buffer is big enough to hold the entire path
        if (len + relPathLen > resultBufLen)
        {
            return -1;
        }

        resultBuf[len - 1] = '/';
        strncpy(resultBuf + len, relPath, relPathLen);
    }

    resultBuf[len + relPathLen] = '\0';
    return 0;
}

int Listeners::buildxl_file_op_listener(kauth_cred_t credential,
                                       void *idata,
                                       kauth_action_t action,
                                       uintptr_t arg0,
                                       uintptr_t arg1,
                                       uintptr_t arg2,
                                       uintptr_t arg3)
{
    BuildXLSandbox *sandbox = OSDynamicCast(BuildXLSandbox, reinterpret_cast<OSObject *>(idata));
    ProcessObject *pCurrentProcess = sandbox->FindTrackedProcess(proc_selfpid());
    if (pCurrentProcess == nullptr)
    {
        return KAUTH_RESULT_DEFER;
    }

    if (CheckDisableDetours(pCurrentProcess->getFamFlags()))
    {
        return KAUTH_RESULT_DEFER;
    }

    FileOpHandler fileOpHandler = FileOpHandler(pCurrentProcess, sandbox);
    return fileOpHandler.HandleFileOpEvent(credential, idata, action, arg0, arg1, arg2, arg3);
}

#pragma mark Scope VNode Callbacks

int Listeners::buildxl_vnode_listener(kauth_cred_t credential,
                                     void *idata,
                                     kauth_action_t action,
                                     uintptr_t arg0,
                                     uintptr_t arg1,
                                     uintptr_t arg2,
                                     uintptr_t arg3)
{
    /**
     * Skip processing event if:
     * (1) KAUTH_VNODE_ACCESS bit is set (request is advisory rather than authoritative)
     * (2) none of the relevant bits are set
     */

    bool isVnodeAccess = HasAnyFlags(action, KAUTH_VNODE_ACCESS);
    bool hasRelevantVnodeBits = HasAnyFlags(action, RELEVANT_KAUTH_VNODE_BITS);

    if (isVnodeAccess || !hasRelevantVnodeBits)
    {
        return KAUTH_RESULT_DEFER;
    }

    BuildXLSandbox *sandbox = OSDynamicCast(BuildXLSandbox, reinterpret_cast<OSObject *>(idata));
    ProcessObject *pCurrentProcess = sandbox->FindTrackedProcess(proc_selfpid());
    if (pCurrentProcess == nullptr)
    {
        return KAUTH_RESULT_DEFER;
    }

    if (CheckDisableDetours(pCurrentProcess->getFamFlags()))
    {
        return KAUTH_RESULT_DEFER;
    }

    VNodeHandler handler = VNodeHandler(pCurrentProcess, sandbox);
    return handler.HandleVNodeEvent(credential, idata, action,
                                    (vfs_context_t)arg0, (vnode_t)arg1,
                                    (vnode_t)arg2,
                                    arg3);
}

#pragma mark TrustedBSD Callbacks

int Listeners::mpo_vnode_check_lookup_pre(kauth_cred_t cred,
                                          struct vnode *dvp,
                                          struct label *dlabel,
                                          const char *path,
                                          // this is supposed to be pathlen, but it appears to be wrong, so don't use
                                          size_t _)
{
    do
    {
        if (path == nullptr)
        {
            break;
        }

        BuildXLSandbox *sandbox = (BuildXLSandbox*)g_dispatcher;
        ProcessObject *pCurrentProcess = sandbox->FindTrackedProcess(proc_selfpid());
        if (pCurrentProcess == nullptr)
        {
            break;
        }

        if (CheckDisableDetours(pCurrentProcess->getFamFlags()))
        {
            break;
        }

        size_t pathlen = strnlen(path, MAXPATHLEN);
        char fullpath[MAXPATHLEN] = {0};
        int errorCode = ComputeAbsolutePath(dvp, path, pathlen, fullpath, sizeof(fullpath));
        if (errorCode != 0)
        {
            log_error("Could not get vnode path, error code: %#X", errorCode);
            break;
        }

        TrustedBsdHandler(pCurrentProcess, sandbox).HandleLookup(fullpath);
    } while(false);

    return KERN_SUCCESS;
}

int Listeners::mpo_vnode_check_readlink(kauth_cred_t cred, struct vnode *vp, struct label *label)
{
    do
    {
        BuildXLSandbox *sandbox = (BuildXLSandbox*)g_dispatcher;
        ProcessObject *pCurrentProcess = sandbox->FindTrackedProcess(proc_selfpid());
        if (pCurrentProcess == nullptr)
        {
            break;
        }
        
        if (CheckDisableDetours(pCurrentProcess->getFamFlags()))
        {
            break;
        }

        return TrustedBsdHandler(pCurrentProcess, sandbox).HandleReadlink(vp);
    } while(false);
    
    return KERN_SUCCESS;
}

int Listeners::mpo_vnode_check_exec(kauth_cred_t cred,
                                    struct vnode *vp,
                                    struct vnode *scriptvp,
                                    struct label *vnodelabel,
                                    struct label *scriptlabel,
                                    struct label *execlabel,
                                    struct componentname *cnp,
                                    u_int *csflags,
                                    void *macpolicyattr,
                                    size_t macpolicyattrlen)
{
    BuildXLSandbox *sandbox = (BuildXLSandbox*)g_dispatcher;
    ProcessObject *rootProcess = sandbox->FindTrackedProcess(proc_selfppid());
    if (rootProcess)
    {
        // report child process to BuildXL only (tracking happens on 'fork's not 'exec's)
        char absExecPath[MAXPATHLEN];
        int len = sizeof(absExecPath);
        vn_getpath(vp, absExecPath, &len);
        AccessHandler handler = AccessHandler(rootProcess, sandbox);
        handler.ReportChildProcessSpwaned(proc_selfpid(), absExecPath);
    }

    return KERN_SUCCESS;
}

void Listeners::mpo_proc_notify_exit(proc_t proc)
{
    pid_t pid = proc_pid(proc);
    BuildXLSandbox *sandbox = (BuildXLSandbox*)g_dispatcher;
    ProcessObject *trackedProcess = sandbox->FindTrackedProcess(pid);
    if (trackedProcess)
    {
        trackedProcess->retain();
        AccessHandler handler = AccessHandler(trackedProcess, sandbox);
        handler.ReportProcessExited(pid);
        sandbox->UntrackProcess(pid, trackedProcess);
        trackedProcess->release();
    }
}

int Listeners::mpo_cred_label_update_execve(kauth_cred_t old_cred,
                                             kauth_cred_t new_cred,
                                             struct proc *p,
                                             struct vnode *vp,
                                             off_t offset,
                                             struct vnode *scriptvp,
                                             struct label *vnodelabel,
                                             struct label *scriptvnodelabel,
                                             struct label *execlabel,
                                             u_int *csflags,
                                             void *macpolicyattr,
                                             size_t macpolicyattrlen,
                                             int *disjointp)
{
    // Track vfork(), make sure not to re-add the tracked process as this handler also gets called on execve()
    mpo_cred_label_associate_fork(old_cred, p);
    return KERN_SUCCESS;
}

void Listeners::mpo_cred_label_associate_fork(kauth_cred_t cred, proc_t proc)
{
    BuildXLSandbox *sandbox = (BuildXLSandbox*)g_dispatcher;
    int pid = proc_pid(proc);
    int ppid = proc_ppid(proc);

    ProcessObject *trackedProcess = sandbox->FindTrackedProcess(ppid);
    if (trackedProcess)
    {
        // parent is tracked --> track this one too
        if (sandbox->TrackChildProcess(pid, trackedProcess))
        {
            AccessHandler handler = AccessHandler(trackedProcess, sandbox);

            char procName[MAXPATHLEN] = {0};
            proc_name(pid, procName, sizeof(procName));
            handler.ReportChildProcessSpwaned(pid, procName);
        }
    }
}

int Listeners::mpo_vnode_check_create(kauth_cred_t cred,
                                      struct vnode *dvp,
                                      struct label *dlabel,
                                      struct componentname *cnp,
                                      struct vnode_attr *vap)
{
    BuildXLSandbox *sandbox = (BuildXLSandbox*)g_dispatcher;
    ProcessObject *trackedProcess = sandbox->FindTrackedProcess(proc_selfpid());
    if (trackedProcess)
    {
        // compute full path by getting the absolute path of 'dvp' and appending the component name provided by 'cnp'
        char path[MAXPATHLEN] = {0};
        ComputeAbsolutePath(dvp, cnp->cn_nameptr, cnp->cn_namelen, path, sizeof(path));

        // check if the target is a directory
        bool isDir = vap->va_type == VDIR;
        bool isSymlink = vap->va_type == VLNK;

        return TrustedBsdHandler(trackedProcess, sandbox)
            .HandleVNodeCreateEvent(path, isDir, isSymlink);
    }

    return KERN_SUCCESS;
}
