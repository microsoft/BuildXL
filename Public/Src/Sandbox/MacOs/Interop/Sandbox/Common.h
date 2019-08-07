// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Common_hpp
#define Common_hpp

#include "BuildXLSandboxShared.hpp"

#define REPORT_QUEUE_SUCCESS                      0x1000
#define REPORT_QUEUE_CONNECTION_ERROR             0x1001
#define REPORT_QUEUE_DEQUEUE_ERROR                0x1002

typedef enum {
    Kext,
    EndpointSecurity
} ConnectionType;

extern "C"
{
    void SetLogger(os_log_t newLogger);

    /*!
     * Normalized path is stored in 'buffer'.  That buffer must be 'bufferSize' bytes long.
     *
     * @param path Path to normalize and hash.
     * @param buffer Buffer where the normalized path is stored.
     * @param bufferSize The size of 'buffer' in bytes.
     * @result Hash of the normalized path.
     */
    int NormalizePathAndReturnHash(const char *path, char *buffer, int bufferSize);

    typedef void (__cdecl *AccessReportCallback)(AccessReport, int);

    bool SendPipStarted(const pid_t processId, pipid_t pipId, const char *const famBytes, int famBytesLength, ConnectionType type, void *connection);
    bool SendPipProcessTerminated(pipid_t pipId, pid_t processId, ConnectionType type, void *connection);
};

#endif /* Common_hpp */
