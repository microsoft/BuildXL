//
//  VNodeHandler.cpp
//  VNodeHandler
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include <sys/vnode.h>
#include "VNodeHandler.hpp"
#include "OpNames.hpp"

typedef struct {
    int action;
    char *nameIfFile;
    char *nameIfDir;
} VNodeMetaInfo;

/** Meta information for all defined VNODE actions */
static const VNodeMetaInfo g_allActions[] = {
    {
        KAUTH_VNODE_READ_DATA,
        (char*) "READ_DATA",
        (char*) "LIST_DIRECTORY"

    },
    {
        KAUTH_VNODE_WRITE_DATA,
        (char*) "WRITE_DATA",
        (char*) "ADD_FILE"

    },
    {
        KAUTH_VNODE_EXECUTE,
        (char*) "EXECUTE",
        (char*) "SEARCH"

    },
    {
        KAUTH_VNODE_DELETE,
        (char*) "DELETE"

    },
    {
        KAUTH_VNODE_APPEND_DATA,
        (char*) "APPEND_DATA",
        (char*) "ADD_SUBDIRECTORY"

    },
    { KAUTH_VNODE_DELETE_CHILD,        (char*) "DELETE_CHILD" },
    { KAUTH_VNODE_READ_ATTRIBUTES,     (char*) "READ_ATTRIBUTES" },
    { KAUTH_VNODE_WRITE_ATTRIBUTES,    (char*) "WRITE_ATTRIBUTES" },
    { KAUTH_VNODE_READ_EXTATTRIBUTES,  (char*) "READ_EXTATTRIBUTES" },
    { KAUTH_VNODE_WRITE_EXTATTRIBUTES, (char*) "WRITE_EXTATTRIBUTES" },
    { KAUTH_VNODE_READ_SECURITY,       (char*) "READ_SECURITY" },
    { KAUTH_VNODE_WRITE_SECURITY,      (char*) "WRITE_SERCURITY" },
    { KAUTH_VNODE_TAKE_OWNERSHIP,      (char*) "TAKE_OWNERSHIP" },
    { KAUTH_VNODE_SYNCHRONIZE,         (char*) "SYNCHRONIZE" },
    { KAUTH_VNODE_LINKTARGET,          (char*) "LINKTARGET" },
    { KAUTH_VNODE_CHECKIMMUTABLE,      (char*) "CHECKIMMUTABLE" },
    { KAUTH_VNODE_ACCESS,              (char*) "ACCESS" },
};

char *GetName(VNodeMetaInfo vnodeInfo, bool isDir)
{
    return isDir
        ? (vnodeInfo.nameIfDir != nullptr ? vnodeInfo.nameIfDir : vnodeInfo.nameIfFile)
        : vnodeInfo.nameIfFile;
}

/**
 * Constructs a descriptive string describing all flags that are contained in
 * a given 'action'.
 *
 * It allocates kernel memory to hold the resulting string; the client is
 * responsible for freeing it by calling IOFree on the result.
 *
 * The resulting string is stored in *outStrPtr, and its length is stored
 * in *outStrLenPtr.
 *
 * NOTE: this is only useful when debugging the sandbox kernel extension, i.e., it is not needed
 *       during the regular mode of operation (sandbox kernel extension sending reports to Domino).
 */
bool ConstructVNodeActionString(kauth_action_t action,
                                bool isDir,
                                const char *separator,
                                char *result,
                                int *resultLength)
{
    int numActions = sizeof(g_allActions)/sizeof(g_allActions[0]);

    // pass 1: discover matches, store them in an array, compute total string size
    int numMatches = 0;
    int sumOfPresentFlagNameLengths = 0;
    struct Match { char *name; size_t len; } matches[numActions];
    for (int i = 0; i < numActions; i++) {
        if (HasAnyFlags(action, g_allActions[i].action)) {
            char *name = GetName(g_allActions[i], isDir);
            size_t nameLength = strlen(name);
            matches[numMatches++] = { name, nameLength };
            sumOfPresentFlagNameLengths += nameLength;
        }
    }

    // pass 2: construct the final string by iterating through matches
    int sepLength = (int)strlen(separator);

    int realResultLen = sumOfPresentFlagNameLengths + MAX(numMatches - 1, 0)*sepLength + 1;
    if (realResultLen > *resultLength) return false;

    *resultLength = realResultLen;
    result[*resultLength - 1] = '\0';

    char *dest = result;
    for (int i = 0; i < numMatches; i++) {
        if (i > 0) {
            memcpy(dest, separator, sepLength);
            dest += sepLength;
        }
        memcpy(dest, matches[i].name, matches[i].len);
        dest += matches[i].len;
    }

    return true;
}

/**
 * Creates a full path for a vnode.  'vp' may be NULL, in which
 * case the returned path is NULL (that is, no memory is allocated).
 * The caller is responsible for handling this memory.
 *
 * The return value indicates if the operation succeeded (i.e.,
 * whether 'result' contains the requested path).
 */
bool VNodeHandler::CreateVnodePath(vnode_t vp, char *result, int len)
{
    if (vp == nullptr || result == nullptr)
    {
        return false;
    }

    int errorCode = vn_getpath(vp, result, &len);
    if (errorCode != 0)
    {
        log_debug("vn_getpath failed with error code %#X", errorCode);
    }

    return errorCode == 0;
}

static bool ShouldDeny(AccessCheckResult accessCheck)
{
    return accessCheck.ShouldDenyAccess();
}

int VNodeHandler::HandleVNodeEvent(const kauth_cred_t credential,
                                   const void *idata,
                                   const kauth_action_t action,
                                   const vfs_context_t context,
                                   const vnode_t vp,
                                   const vnode_t dvp,
                                   const uintptr_t arg3)
{
    boolean_t isDir = vnode_isdir(vp);

    int len = MAXPATHLEN;
    char path[MAXPATHLEN] = {0};
    if (!CreateVnodePath(vp, path, len))
    {
        return KAUTH_RESULT_DEFER;
    }

    PolicyResult policyResult = PolicyForPath(path);

    const int readAttrFlags = KAUTH_VNODE_READ_ATTRIBUTES |
                              KAUTH_VNODE_READ_EXTATTRIBUTES |
                              KAUTH_VNODE_READ_SECURITY;

    if ((
            HasAnyFlags(action, readAttrFlags) &&
            ShouldDeny(CheckProbe(policyResult, isDir))
        ) || (
            HasAnyFlags(action, KAUTH_VNODE_EXECUTE) &&
            ShouldDeny(CheckExecute(policyResult, isDir))
        ) || (
            HasAnyFlags(action, KAUTH_VNODE_READ_DATA) &&
            ShouldDeny(CheckRead(policyResult, isDir))
        ) || (
            HasAnyFlags(action, KAUTH_VNODE_GENERIC_WRITE_BITS) &&
            ShouldDeny(CheckWrite(policyResult, isDir))
        ))
    {
        LogAccessDenied(path, action);
        return KAUTH_RESULT_DENY;
    }

    return KAUTH_RESULT_DEFER;
}

AccessCheckResult VNodeHandler::CheckExecute(PolicyResult policyResult, bool isDir)
{
    RequestedReadAccess requestedAccess = isDir
        ? RequestedReadAccess::Probe
        : RequestedReadAccess::Read;

    AccessCheckResult checkResult = policyResult.CheckReadAccess(
        requestedAccess,
        FileReadContext(FileExistence::Existent, isDir));

    FileOperationContext fop = ToFileContext(
        OpKAuthVNodeExecute,
        GENERIC_READ | GENERIC_EXECUTE,
        CreationDisposition::OpenExisting,
        policyResult.Path());

    Report(fop, policyResult, checkResult);

    return checkResult;
}

AccessCheckResult VNodeHandler::CheckProbe(PolicyResult policyResult, bool isDir)
{
    AccessCheckResult checkResult = policyResult.CheckReadAccess(
        RequestedReadAccess::Probe,
        FileReadContext(FileExistence::Existent, isDir));

    FileOperationContext fop = FileOperationContext::CreateForRead(OpKAuthVNodeProbe, policyResult.Path());
    Report(fop, policyResult, checkResult);

    return checkResult;
}

AccessCheckResult VNodeHandler::CheckRead(PolicyResult policyResult, bool isDir)
{
    RequestedReadAccess requestedAccess = isDir
        ? RequestedReadAccess::Enumerate
        : RequestedReadAccess::Read;

    AccessCheckResult checkResult = policyResult.CheckReadAccess(
        requestedAccess,
        FileReadContext(FileExistence::Existent, isDir));

    FileOperationContext fop = FileOperationContext::CreateForRead(OpKAuthVNodeRead, policyResult.Path());
    Report(fop, policyResult, checkResult);

    return checkResult;
}

AccessCheckResult VNodeHandler::CheckWrite(PolicyResult policyResult, bool isDir)
{
    AccessCheckResult checkResult = isDir
        ? policyResult.CheckReadAccess(RequestedReadAccess::Probe, FileReadContext(FileExistence::Existent, isDir))
        : policyResult.CheckWriteAccess();

    FileOperationContext fop = ToFileContext(OpKAuthVNodeWrite,
                                             GENERIC_WRITE,
                                             CreationDisposition::CreateAlways,
                                             policyResult.Path());

    Report(fop, policyResult, checkResult);
    return checkResult;
}
