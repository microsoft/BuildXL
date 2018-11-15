//
//  TrustedBsdHandler.cpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "TrustedBsdHandler.hpp"
#include "OpNames.hpp"

int TrustedBsdHandler::HandleLookup(const char *path)
{
    PolicyResult policyResult = PolicyForPath(path);

    AccessCheckResult checkResult = policyResult.CheckReadAccess(
        RequestedReadAccess::Probe, FileReadContext(FileExistence::Nonexistent));

    FileOperationContext fOp = FileOperationContext::CreateForRead(OpMacLookup, path);

    const OSSymbol *cacheKey = OSSymbol::withCString(path);
    Report(fOp, policyResult, checkResult, 0, cacheKey);
    OSSafeReleaseNULL(cacheKey);

    // Never deny lookups
    return KERN_SUCCESS;
}

int TrustedBsdHandler::HandleReadlink(vnode_t symlinkVNode)
{
    // get symlink path
    char path[MAXPATHLEN];
    int len = MAXPATHLEN;
    int err = vn_getpath(symlinkVNode, path, &len);
    if (err)
    {
        log_error("Could not get VNnode path for readlink operation; error code: %#X", err);
        return KERN_SUCCESS; // don't deny access because of our own error
    }
    
    // check read access
    PolicyResult policyResult = PolicyForPath(path);
    AccessCheckResult checkResult = policyResult.CheckExistingFileReadAccess();
    FileOperationContext fOp = FileOperationContext::CreateForRead(OpMacReadlink, path);
    Report(fOp, policyResult, checkResult);
    
    if (checkResult.ShouldDenyAccess())
    {
        LogAccessDenied(path, 0, "Operation: Readlink");
        return EPERM;
    }
    else
    {
        return KERN_SUCCESS;
    }
}

int TrustedBsdHandler::HandleVNodeCreateEvent(const char *fullPath, const bool isDir, const bool isSymlink)
{
    PolicyResult policyResult = PolicyForPath(fullPath);
    AccessCheckResult result = CheckCreate(policyResult, isDir, isSymlink);
    if (result.ShouldDenyAccess())
    {
        LogAccessDenied(fullPath, 0, "Operation: VNodeCreate");
        return EPERM;
    }
    else
    {
        return KERN_SUCCESS;
    }
}

AccessCheckResult TrustedBsdHandler::CheckCreate(PolicyResult policyResult, bool isDir, bool isSymlink)
{
    AccessCheckResult checkResult =
        isSymlink ? policyResult.CheckSymlinkCreationAccess() :
        isDir     ? policyResult.CheckDirectoryAccess(CheckDirectoryCreationAccessEnforcement(GetFamFlags())) :
                    policyResult.CheckWriteAccess();
    
    FileOperationContext fop = ToFileContext(OpMacVNodeCreate,
                                             GENERIC_WRITE,
                                             CreationDisposition::CreateAlways,
                                             policyResult.Path());
    
    Report(fop, policyResult, checkResult);
    return checkResult;
}
