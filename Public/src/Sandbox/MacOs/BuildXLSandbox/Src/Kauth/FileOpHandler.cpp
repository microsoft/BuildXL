//
//  FileOpHandler.cpp
//  FileOpHandler
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "FileOpHandler.hpp"
#include "OpNames.hpp"

void FileOpHandler::ReportFileOpAccess(const char *operationName,
                                       DWORD desiredAccess,
                                       DWORD creationDisposition,
                                       AccessCheckResult accessCheckResult,
                                       PolicyResult policyResult)
{
    FileOperationContext fOp =
        FileOperationContext(operationName, desiredAccess, 0,
                             creationDisposition, 0, policyResult.Path());

    Report(fOp, policyResult, accessCheckResult);
}

void FileOpHandler::HandleFileRename(const kauth_cred_t credential,
                                     const void *data,
                                     PolicyResult srcPath,
                                     PolicyResult destPath)
{
    ReportFileOpAccess(OpKAuthMoveSource,
                       GENERIC_READ | GENERIC_WRITE | WIN_DELETE,
                       OpenExisting,
                       srcPath.CheckExistingFileReadAccess(),
                       srcPath);

    ReportFileOpAccess(OpKAuthMoveDest, GENERIC_WRITE, CreateNew,
                       destPath.CheckWriteAccess(), destPath);
}

void FileOpHandler::HandleCreateLink(const kauth_cred_t credential,
                                     const void *data,
                                     PolicyResult srcPath,
                                     PolicyResult destPath)
{
    ReportFileOpAccess(OpKAuthCreateHardlinkSource, GENERIC_READ, OpenExisting,
                       srcPath.CheckExistingFileReadAccess(), srcPath);

    AccessCheckResult result = destPath.CheckWriteAccess();

    ReportFileOpAccess(OpKAuthCreateHardlinkDest, GENERIC_WRITE, CreateNew,
                       result, destPath);
}

void FileOpHandler::HandleCopyFile(const kauth_cred_t credential,
                                   const void *data,
                                   PolicyResult srcPath,
                                   PolicyResult destPath)
{
    ReportFileOpAccess(OpKAuthCopySource, GENERIC_READ, OpenExisting,
                       srcPath.CheckExistingFileReadAccess(), srcPath);

    ReportFileOpAccess(OpKAuthCopyDest, GENERIC_WRITE, CreateNew,
                       destPath.CheckWriteAccess(), destPath);
}

void FileOpHandler::HandleFileExchange(const kauth_cred_t credential,
                                       const void *data,
                                       PolicyResult srcPath,
                                       PolicyResult destPath)
{
    HandleCopyFile(credential, data, srcPath, destPath);
    HandleCopyFile(credential, data, destPath, srcPath);
}

void FileOpHandler::HandleDelete(const kauth_cred_t credential,
                                 const void *data,
                                 bool isDirectory,
                                 PolicyResult path)
{
    const char *operation = isDirectory
        ? OpKAuthDeleteDir
        : OpKAuthDeleteFile;

    ReportFileOpAccess(operation, WIN_DELETE, OpenExisting,
                       path.CheckWriteAccess(), path);
}

void FileOpHandler::HandleRead(const kauth_cred_t credential,
                               const void* data,
                               bool isDirectory,
                               PolicyResult policyResult)
{
    if (isDirectory)
    {
        AccessCheckResult result = AccessCheckResult(
            RequestedAccess::Enumerate,
            ResultAction::Allow,
            policyResult.ReportDirectoryEnumeration() ? ReportLevel::ReportExplicit : ReportLevel::Ignore);

        ReportFileOpAccess(OpKAuthOpenDir, GENERIC_READ, OpenExisting, result, policyResult);
    }
    else
    {
        Report(FileOperationContext::CreateForRead(OpKAuthReadFile, policyResult.Path()),
               policyResult,policyResult.CheckExistingFileReadAccess());
    }
}

void FileOpHandler::HandleWrite(const kauth_cred_t credential,
                                const void* data,
                                bool isDirectory,
                                PolicyResult policyResult)
{
    const char *opName = isDirectory ? OpKAuthCreateDir : OpKAuthWriteFile;
    Report(FileOperationContext::CreateForWrite(opName, policyResult.Path()), policyResult, policyResult.CheckWriteAccess());
}

int FileOpHandler::HandleFileOpEvent(const kauth_cred_t credential,
                                     const void *data,
                                     const kauth_action_t action,
                                     const uintptr_t arg0,
                                     const uintptr_t arg1,
                                     const uintptr_t arg2,
                                     const uintptr_t arg3)
{
    PolicyResult arg1Policy = PolicyForPath((char*) arg1);
    vnode_t vp = nullptr;

    switch (action)
    {
        case KAUTH_FILEOP_RENAME:
        {
            HandleFileRename(credential, data, PolicyForPath((char*) arg0),
                             arg1Policy);
            break;
        }
        case KAUTH_FILEOP_LINK:
        {
            HandleCreateLink(credential, data, PolicyForPath((char*) arg0),
                             arg1Policy);
            break;
        }
        case KAUTH_FILEOP_EXCHANGE:
        {
            HandleFileExchange(credential, data, PolicyForPath((char*) arg0),
                               arg1Policy);
            break;
        }
        case KAUTH_FILEOP_DELETE:
        {
            vp = (vnode_t)arg0;
            HandleDelete(credential, data, vnode_isdir(vp), arg1Policy);

            break;
        }
        case KAUTH_FILEOP_OPEN:
        {
            vp = (vnode_t)arg0;
            HandleRead(credential, data, vnode_isdir(vp), arg1Policy);

            break;
        }
        case KAUTH_FILEOP_CLOSE:
        {
            vp = (vnode_t)arg0;
            if ((int)arg2 == KAUTH_FILEOP_CLOSE_MODIFIED)
            {
                HandleWrite(credential, data, vnode_isdir(vp), arg1Policy);
            }
            else
            {
                HandleRead(credential, data, vnode_isdir(vp), arg1Policy);
            }

            break;
        }
        default:
            break;
    }

    return KAUTH_RESULT_DEFER;
}
