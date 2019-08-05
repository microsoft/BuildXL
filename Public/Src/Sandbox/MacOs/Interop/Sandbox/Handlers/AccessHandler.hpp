// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef AccessHandler_hpp
#define AccessHandler_hpp

#ifdef ES_SANDBOX

#include "ESSandbox.h"
#include "SandboxedPip.hpp"
#include "Checkers.hpp"
#include "OpNames.hpp"

enum ReportResult
{
    kReported,
    kSkipped,
    kFailed
};

typedef bool (Handler)(void *data);

struct AccessHandler
{
private:

    ESSandbox *sandbox_;

    SandboxedProcess *process_;

    uint64_t creationTimestamp_;

    ReportResult ReportFileOpAccess(FileOperation operation,
                                    PolicyResult policy,
                                    AccessCheckResult accessCheckResult,
                                    pid_t processID);

    inline void SetProcess(SandboxedProcess *process) { process_ = process; }

protected:

    ESSandbox* GetSandbox()   const { return sandbox_; }
    SandboxedProcess* GetProcess() const { return process_; }
    SandboxedPip* GetPip()         const { return process_->getPip(); }

    PolicySearchCursor FindManifestRecord(const char *absolutePath, size_t pathLength = -1);
    
    /*!
     * Copies 'process_->getPath()' into 'report->path'.
     */
    void SetProcessPath(AccessReport *report);

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
     * @param msg The EndpointSecurity message containing all necessary details about the observed event
     * @param isDir Indicates if the report is being generated for a directory or file
     */
    AccessCheckResult CheckAndReportInternal(FileOperation operation,
                                     const char *path,
                                     CheckFunc checker,
                                     const es_message_t *msg,
                                     bool isDir);

    AccessCheckResult CheckAndReport(FileOperation operation, const char *path, CheckFunc checker, const es_message_t *msg)
    {
        return CheckAndReportInternal(operation, path, checker, msg, false);
    }

    AccessCheckResult CheckAndReport(FileOperation operation, const char *path, CheckFunc checker, const es_message_t *msg, bool isDir)
    {
        return CheckAndReportInternal(operation, path, checker, msg, isDir);
    }

public:

    AccessHandler(ESSandbox *sandbox)
    {
        creationTimestamp_ = mach_absolute_time();
        sandbox_           = sandbox;
        process_           = nullptr;
    }

    ~AccessHandler()
    {
        sandbox_ = nullptr;
        process_ = nullptr;
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
    inline pid_t GetProcessId()                 const { return GetPip()->getProcessId(); }
    inline pipid_t GetPipId()                   const { return GetPip()->getPipId(); }
    inline int GetProcessTreeSize()             const { return GetPip()->getTreeSize(); }
    inline FileAccessManifestFlag GetFamFlags() const { return GetPip()->getFamFlags(); }

    PolicyResult PolicyForPath(const char *absolutePath);

    bool ReportProcessTreeCompleted(pid_t processId);
    bool ReportProcessExited(pid_t childPid);
    bool ReportChildProcessSpawned(pid_t childPid);
};

#ifdef ES_SANDBOX

#endif /* AccessHandler_hpp */
