//
//  FileOpHandler.hpp
//  FileOpHandler
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef FileOpHandler_hpp
#define FileOpHandler_hpp

#include "AccessHandler.hpp"

class FileOpHandler : public AccessHandler
{
    public:

        FileOpHandler(const ProcessObject *process, DominoSandbox *sandbox) :
            AccessHandler(process, sandbox) { }

        void ReportFileOpAccess(const char *operationName,
                                DWORD desiredAccess,
                                DWORD creationDisposition,
                                AccessCheckResult accessCheckResult,
                                PolicyResult policyResult);

        void HandleFileRename(const kauth_cred_t credential,
                              const void *data,
                              PolicyResult srcPath,
                              PolicyResult destPath);

        void HandleCreateLink(const kauth_cred_t credential,
                              const void *data,
                              PolicyResult srcPath,
                              PolicyResult destPath);

        void HandleCopyFile(const kauth_cred_t credential,
                            const void *data,
                            PolicyResult srcPath,
                            PolicyResult destPath);

        void HandleFileExchange(const kauth_cred_t credential,
                                const void *data,
                                PolicyResult srcPath,
                                PolicyResult destPath);

        void HandleDelete(const kauth_cred_t credential,
                          const void *data,
                          bool isDirectory,
                          PolicyResult path);

        void HandleRead(const kauth_cred_t credential,
                        const void *data,
                        bool isDirectory,
                        PolicyResult path);

        void HandleWrite(const kauth_cred_t credential,
                         const void *data,
                         bool isDirectory,
                         PolicyResult path);

        int HandleFileOpEvent(const kauth_cred_t credential,
                              const void *idata,
                              const kauth_action_t action,
                              const uintptr_t arg0,
                              const uintptr_t arg1,
                              const uintptr_t arg2,
                              const uintptr_t arg3);
};

#endif /* FileOpHandler_hpp */
