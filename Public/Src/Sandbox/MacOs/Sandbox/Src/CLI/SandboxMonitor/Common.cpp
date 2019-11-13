// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Common.h"
#include "Sandbox.h"
#include "ESSandbox.h"

void SetLogger(os_log_t newLogger)
{
    logger = newLogger;
}

extern "C"
{
#pragma mark Pip status functions

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

#pragma mark Exported interop functions

    int NormalizePathAndReturnHash(const BYTE *path, BYTE *buffer, int bufferSize)
    {
        return NormalizeAndHashPath((PCPathChar)path, buffer, bufferSize);
    }
}
