// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "SandboxedPip.hpp"

SandboxedPip::SandboxedPip(pid_t pid, const char *payload, size_t length)
{
    payload_ = (char *) malloc(length);
    if (payload == NULL)
    {
        throw "Could not allocate memory for payload storage!";
    }
    
    processId_ = pid;
    processTreeCount_ = 1;
    memcpy(payload_, payload, length);
    fam_.init((BYTE*)payload_, length);
    if (fam_.HasErrors())
    {
        throw "FileAccessManifest parsing exception";
    }
}

SandboxedPip::~SandboxedPip()
{
    free(payload_);
}

