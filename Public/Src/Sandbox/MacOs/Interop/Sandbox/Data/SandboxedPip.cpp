// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "SandboxedPip.hpp"
#include "BuildXLException.hpp"

#pragma mark SandboxedPip Implementation

SandboxedPip::SandboxedPip(pid_t pid, const char *payload, size_t length)
{
    log_debug("Initializing with pid (%d) from: %{public}s", pid, __FUNCTION__);
    
    payload_ = (char *) malloc(length);
    if (payload == NULL)
    {
        throw BuildXLException("Could not allocate memory for FAM payload storage!");
    }
    
    memcpy(payload_, payload, length);
    fam_.init((BYTE*)payload_, length);
    
    if (fam_.HasErrors())
    {
        std::string error= "FileAccessManifest parsing exception, error: ";
        throw BuildXLException(error.append(fam_.Error()));
    }
    
    processId_ = pid;
    processTreeCount_ = 1;
}

SandboxedPip::~SandboxedPip()
{
    log_debug("Releasing pip object (%#llX) - freed from %{public}s", GetPipId(),  __FUNCTION__);
    free(payload_);
}
