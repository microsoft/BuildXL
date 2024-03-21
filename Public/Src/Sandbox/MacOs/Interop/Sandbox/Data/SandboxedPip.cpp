// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "SandboxedPip.hpp"
#include "BuildXLException.hpp"

#pragma mark SandboxedPip Implementation

SandboxedPip::SandboxedPip(pid_t pid, char *payload, size_t length)
{
    log_debug("Initializing with pid (%d) from: %{public}s", pid, __FUNCTION__);

    // If an error occurs with FAM parsing, then an assertion will be thrown
    fam_ = std::make_unique<buildxl::common::FileAccessManifest>(payload, length);

    processId_ = pid;
    processTreeCount_ = 1;
}

SandboxedPip::~SandboxedPip()
{
    log_debug("Releasing pip object (%#llX) - freed from %{public}s", GetPipId(),  __FUNCTION__);
    free(payload_);
}
