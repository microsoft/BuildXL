// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Common.h"

#if !SB_MONITOR
#include "KextSandbox.h"
#include "ESSandbox.h"
#endif

os_log_t logger = os_log_create(kBuildXLBundleIdentifier, "Logger");

extern "C"
{
    void SetLogger(os_log_t newLogger)
    {
        logger = newLogger;
    }

#pragma mark Pip status functions
#if !SB_MONITOR
    bool SendPipStarted(const pid_t processId, pipid_t pipId, const char *const famBytes, int famBytesLength, ConnectionType type, void *connection)
    {
        switch (type)
        {
            case Kext:
            {
                KextConnectionInfo *context = (KextConnectionInfo *) connection;
                return KEXT_SendPipStarted(processId, pipId, famBytes, famBytesLength, *context);
            }
            case EndpointSecurity:
                return ES_SendPipStarted(processId, pipId, famBytes, famBytesLength);
        }
    
        return false;
    }

    bool SendPipProcessTerminated(pipid_t pipId, pid_t processId, ConnectionType type, void *connection)
    {
        switch (type)
        {
            case Kext:
            {
                KextConnectionInfo *context = (KextConnectionInfo *) connection;
                return KEXT_SendPipProcessTerminated(pipId, processId, *context);
            }
            case EndpointSecurity:
                return ES_SendPipProcessTerminated(pipId, processId);
        }
    
        return false;
    }
#endif
#pragma mark Exported interop functions

    int NormalizePathAndReturnHash(const BYTE *path, BYTE *buffer, int bufferSize)
    {
        return NormalizeAndHashPath((PCPathChar)path, buffer, bufferSize);
    }
}
