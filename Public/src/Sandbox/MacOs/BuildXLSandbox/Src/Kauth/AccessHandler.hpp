//
//  AccessHandler.hpp
//  AccessHandler
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef AccessHandler_hpp
#define AccessHandler_hpp

#include "ProcessObject.hpp"
#include "BuildXLSandbox.hpp"

enum ReportResult
{
    kReported,
    kSkipped,
    kFailed
};

inline bool HasAnyFlags(const int source, const int bitMask)
{
    return (source & bitMask) != 0;
}

class AccessHandler
{
private:

    const ProcessObject *process_;

    // TODO: Can we solve this nicer?  Maybe pass a function pointer to SendAccessReport only?
    DominoSandbox *sandbox_;

    ReportResult DoReport(FileOperationContext fileOperationCtx,
                          PolicyResult policyResult,
                          AccessCheckResult checkResult,
                          DWORD error = 0,
                          const OSSymbol *cacheKey = nullptr);

protected:

    PolicySearchCursor FindManifestRecord(const char *absolutePath,
                                          size_t pathLength = -1);

    FileOperationContext ToFileContext(const char *action,
                                       DWORD requestedAccess,
                                       CreationDisposition disposition,
                                       const char *path);

    ReportResult Report(FileOperationContext fileOperationCtx,
                        PolicyResult policyResult,
                        AccessCheckResult checkResult,
                        DWORD error = 0,
                        const OSSymbol *cacheKey = nullptr);

    void LogAccessDenied(const char *path,
                         kauth_action_t action,
                         const char *errorMessage = "");

public:

    AccessHandler(const ProcessObject *process, DominoSandbox *sandbox)
        : sandbox_(sandbox), process_(process) { }

    inline pid_t GetClientPid()                 const { return process_->getClientPid(); }
    inline pid_t GetProcessId()                 const { return process_->getProcessId(); }
    inline pipid_t GetPipId()                   const { return process_->getPipId(); }
    inline FileAccessManifestFlag GetFamFlags() const { return process_->getFamFlags(); }

    PolicyResult PolicyForPath(const char *absolutePath);

    bool ReportProcessTreeCompleted();
    bool ReportProcessExited(pid_t childPid);
    bool ReportChildProcessSpwaned(pid_t childPid, const char *childProcessPath);
};

#endif /* AccessHandler_hpp */
