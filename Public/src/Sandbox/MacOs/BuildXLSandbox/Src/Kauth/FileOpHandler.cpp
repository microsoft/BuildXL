//
//  FileOpHandler.cpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "FileOpHandler.hpp"
#include "OpNames.hpp"

int FileOpHandler::HandleFileOpEvent(const kauth_cred_t credential,
                                     const void *data,
                                     const kauth_action_t action,
                                     const uintptr_t arg0,
                                     const uintptr_t arg1,
                                     const uintptr_t arg2,
                                     const uintptr_t arg3)
{
    const char *arg1path = (char*) arg1;
    vfs_context_t ctx = vfs_context_create(nullptr);
    vnode_t vp = nullptr;

    switch (action)
    {
        case KAUTH_FILEOP_RENAME:
        {
            CheckAndReport(kOpKAuthMoveSource, (char*) arg0, Checkers::CheckRead, ctx, vp);
            CheckAndReport(kOpKAuthMoveDest, arg1path, Checkers::CheckWrite, ctx, vp);
            break;
        }
        case KAUTH_FILEOP_LINK:
        {
            CheckAndReport(kOpKAuthCreateHardlinkSource, (char*) arg0, Checkers::CheckRead, ctx, vp);
            CheckAndReport(kOpKAuthCreateHardlinkDest, arg1path, Checkers::CheckWrite, ctx, vp);
            break;
        }
        case KAUTH_FILEOP_EXCHANGE:
        {
            CheckAndReport(kOpKAuthCopySource, (char*) arg0, Checkers::CheckReadWrite, ctx, vp);
            CheckAndReport(kOpKAuthCopyDest, arg1path, Checkers::CheckReadWrite, ctx, vp);
            break;
        }
        case KAUTH_FILEOP_DELETE:
        {
            vp = (vnode_t)arg0;
            FileOperation operation = vnode_isdir(vp) ? kOpKAuthDeleteDir : kOpKAuthDeleteFile;
            CheckAndReport(operation, arg1path, Checkers::CheckWrite, ctx, vp);
            break;
        }
        case KAUTH_FILEOP_OPEN:
        {
            vp = (vnode_t)arg0;
            bool isDir = vnode_isdir(vp);
            
            CheckFunc checker = isDir ? Checkers::CheckEnumerateDir : Checkers::CheckRead;
            FileOperation op  = isDir ? kOpKAuthOpenDir : kOpKAuthReadFile;
            
            CheckAndReport(op, arg1path, checker, ctx, vp);
            break;
        }
        case KAUTH_FILEOP_CLOSE:
        {
            vp = (vnode_t)arg0;
            bool isCloseModified = (int)arg2 == KAUTH_FILEOP_CLOSE_MODIFIED;
            
            CheckFunc checker = isCloseModified ? Checkers::CheckWrite : Checkers::CheckRead;
            FileOperation op  = isCloseModified ? kOpKAuthCloseModified : kOpKAuthClose;
            
            CheckAndReport(op, arg1path, checker, ctx, vp);
            break;
        }
        default:
            break;
    }

    vfs_context_rele(ctx);
    
    // FILEOP events are should never try to return KAUTH_RESULT_DENY
    return KAUTH_RESULT_DEFER;
}
