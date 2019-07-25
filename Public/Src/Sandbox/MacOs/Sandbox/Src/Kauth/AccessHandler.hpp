// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef AccessHandler_hpp
#define AccessHandler_hpp

#include "SandboxedPip.hpp"
#include "BuildXLSandbox.hpp"
#include "Checkers.hpp"
#include "OpNames.hpp"
#include "Stopwatch.hpp"

enum ReportResult
{
    kReported,
    kSkipped,
    kFailed
};

typedef bool (Handler)(void *data);

class AccessHandler
{
private:

    // TODO: Can we solve this nicer?  Maybe pass a function pointer to SendAccessReport only?
    BuildXLSandbox *sandbox_;

    SandboxedProcess *process_;

    uint64_t creationTimestamp_;

    ReportResult ReportFileOpAccess(FileOperation operation,
                                    PolicyResult policy,
                                    AccessCheckResult accessCheckResult,
                                    CacheRecord *cacheRecord = nullptr);

    inline void SetProcess(SandboxedProcess *process)
    {
        OSSafeReleaseNULL(process_);
        process_ = process;
        if (process_)
        {
            process_->retain();
        }
    }

    const char *IgnoreCatalinaDataPartitionPrefix(const char* path);
    const char *kCatalinaDataPartitionPrefix = "/System/Volumes/Data/";
    const size_t kAdjustedCatalinaPrefixLength = strlen("/System/Volumes/Data");

protected:

    BuildXLSandbox* GetSandbox()   const { return sandbox_; }
    SandboxedProcess* GetProcess() const { return process_; }
    SandboxedPip* GetPip()         const { return process_->getPip(); }

    PolicySearchCursor FindManifestRecord(const char *absolutePath, size_t pathLength = -1);

    void LogAccessDenied(const char *path, kauth_action_t action, const char *errorMessage = "");

    /*!
     * Copies 'process_->getPath()' into 'report->path'.
     */
    void SetProcessPath(AccessReport *report);

    /*!
     * Checks access applying the fallback logic for coping with the fact that vn_getpath can return a
     * "wrong" path for a given vnode when there exist multiple hard links to that vnode.
     *
     * This kext intercepts accesses to vnodes and from a vnode it has to reconstruct an absolute path.
     * In presence of hard links, there can exist multiple paths to a single vnode. Obtaining a path for a
     * given vnode is thus ambiguous.
     *
     * To cope with this ambiguity, we remember looked up paths, i.e., paths captured via 'SetLastLookedUpPath'
     * called from the handler for MAC_LOOKUP (because there we get paths as requested by the process).
     *
     * This method first applies a given 'checker' function against a given 'policy' object.  If the access is
     * denied, only then the policy is updated with the last looked up path and the check is performed again.
     *
     * @param vp Vnode corresponding to the path from 'policy->Path()'
     * @param ctx Current VFS context
     * @param checker Checker function to apply to policy
     * @param policy Current policy; can be MUTATED by this method if the first check fails
     * @param result Where the result is stored
     * @result Indicates whether the policy was updated with a new path
     */
    bool CheckAccess(vnode_t vp, vfs_context_t ctx, CheckFunc checker, PolicyResult *policy, AccessCheckResult *result);

    /*!
     * Template for checking and reporting file accesses.
     *
     * Adds caching around the existing checking ('CheckAccess') and reporting ('ReportFileOpAccess') methods.
     *
     * The key used for looking up if the operation was already reported is "<operation>,<path>".
     *
     * If the operation has already been reported (cache hit w.r.t. the aforementioned key), an AccessCheckResult
     * object is returned that indicates that the operation is allowed (@result.ShouldDenyAccess() returns false)
     * and that it should not be reported (@result.ShouldReport() returns false).
     *
     * If the operation has not been reported, 'CheckAccess' and 'ReportFileOpAccess' are called and the result
     * is added to the cache if the returned AccessCheckResult object indicates that the operation should not be denied.
     *
     * @param operation Operation to be executed
     * @param path Absolute path against which the operation is to be executed
     * @param checker Checker function to apply to policy
     * @param ctx (Can be NULL) Current VFS context; if NULL, instead of delegating to 'CheckAccess' (which implements
     *            a fallback logic for files with multiple hard links), 'checker' is called directly.
     * @param vp (Can be NULL) Vnode corresponding to 'policy->Path()'; if NULL, instead of delegating to 'CheckAccess'
     *           (which implements a fallback logic for files with multiple hard links), 'checker' is called directly.
     */
    AccessCheckResult CheckAndReportInternal(FileOperation operation,
                                     const char *path,
                                     CheckFunc checker,
                                     vfs_context_t ctx,
                                     vnode_t vp,
                                     bool isDir);

    AccessCheckResult CheckAndReport(FileOperation operation, const char *path, CheckFunc checker, vfs_context_t ctx, vnode_t vp)
    {
        return CheckAndReportInternal(operation, path, checker, ctx, vp, false);
    }

    AccessCheckResult CheckAndReport(FileOperation operation, const char *path, CheckFunc checker, bool isDir)
    {
        return CheckAndReportInternal(operation, path, checker, nullptr, nullptr, isDir);
    }

public:

    AccessHandler(BuildXLSandbox *sandbox)
    {
        creationTimestamp_ = mach_absolute_time();
        sandbox_           = sandbox;
        process_           = nullptr;
    }

    ~AccessHandler()
    {
        Timespan duration = Timespan::fromNanoseconds(mach_absolute_time() - creationTimestamp_);
        if (process_) GetPip()->Counters()->accessHandler += duration;
        if (sandbox_) sandbox_->Counters()->accessHandler += duration;
        OSSafeReleaseNULL(process_);
    }

    /*!
     * Attempts to find a tracked ProcessObject instance that corresponds to a given 'pid'.
     * If successful, initializes this object with the found ProcessObject.
     *
     * IMPORTANT: This should be the first method to call after upon constructor this object.
     *            Whenever the initialization fails, this object should not be used futher.
     *
     * @param pid Process ID to try to find.
     * @result Indicates whether the initialization was successful.
     */
    bool TryInitializeWithTrackedProcess(pid_t pid);

    inline bool HasTrackedProcess()             const { return process_ != nullptr; }
    inline pid_t GetClientPid()                 const { return GetPip()->getClientPid(); }
    inline pid_t GetProcessId()                 const { return GetPip()->getProcessId(); }
    inline pipid_t GetPipId()                   const { return GetPip()->getPipId(); }
    inline int GetProcessTreeSize()             const { return GetPip()->getTreeSize(); }
    inline FileAccessManifestFlag GetFamFlags() const { return GetPip()->getFamFlags(); }

    PolicyResult PolicyForPath(const char *absolutePath);

    bool ReportProcessTreeCompleted();
    bool ReportProcessExited(pid_t childPid);
    bool ReportChildProcessSpawned(pid_t childPid);
};

#endif /* AccessHandler_hpp */
