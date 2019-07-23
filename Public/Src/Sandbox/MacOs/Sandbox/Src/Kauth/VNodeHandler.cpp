// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <AvailabilityMacros.h>
#include <sys/vnode.h>
#include "VNodeHandler.hpp"
#include "OpNames.hpp"

typedef struct {
#ifdef MAC_OS_X_VERSION_10_15
    uint action;
#else
    int action;
#endif
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
 *       during the regular mode of operation (sandbox kernel extension sending reports to BuildXL).
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

static kauth_action_t KAUTH_VNODE_PROBE_FLAGS = KAUTH_VNODE_READ_ATTRIBUTES | KAUTH_VNODE_READ_EXTATTRIBUTES | KAUTH_VNODE_READ_SECURITY;

static FlagsToCheckFunc s_handlers[]
{
    {
        .flags     = KAUTH_VNODE_PROBE_FLAGS,
        .operation = kOpKAuthVNodeProbe,
        .checker   = Checkers::CheckProbe
    },
    {
        .flags     = KAUTH_VNODE_EXECUTE,
        .operation = kOpKAuthVNodeExecute,
        .checker   = Checkers::CheckExecute
    },
    {
        .flags     = KAUTH_VNODE_READ_DATA,
        .operation = kOpKAuthVNodeRead,
        .checker   = Checkers::CheckRead
    },
    {
        .flags     = KAUTH_VNODE_GENERIC_WRITE_BITS,
        .operation = kOpKAuthVNodeWrite,
        .checker   = Checkers::CheckWrite
    }
};

static int s_handlersCount = sizeof(s_handlers)/sizeof(s_handlers[0]);

int VNodeHandler::HandleVNodeEvent(const kauth_cred_t credential,
                                   const void *idata,
                                   const kauth_action_t action,
                                   const vfs_context_t ctx,
                                   const vnode_t vp,
                                   const vnode_t dvp,
                                   const uintptr_t arg3)
{
    int len = MAXPATHLEN;
    char path[MAXPATHLEN] = {0};

    int errno = vn_getpath(vp, path, &len);
    if (errno != 0)
    {
        return KAUTH_RESULT_DEFER;
    }

    bool shouldDeny = false;

    // even after the first match we have to continue looping because multiple flags can be set in a single action
    for (int i = 0; i < s_handlersCount; i++)
    {
        // skip over handlers that don't apply
        if (!HasAnyFlags(action, s_handlers[i].flags))
        {
            continue;
        }

        AccessCheckResult checkResult = CheckAndReport(s_handlers[i].operation,
                                                       path, s_handlers[i].checker,
                                                       ctx, vp);

        shouldDeny = shouldDeny || checkResult.ShouldDenyAccess();
    }

    if (shouldDeny)
    {
        LogAccessDenied(path, action);
        return KAUTH_RESULT_DENY;
    }
    else
    {
        return KAUTH_RESULT_DEFER;
    }
}
