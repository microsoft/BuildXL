// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "SandboxedProcess.hpp"

#pragma mark SandboxedProcess Implementation

SandboxedProcess::SandboxedProcess(pid_t processId, std::shared_ptr<SandboxedPip> pip)
{
    assert(pip != nullptr);
    log_debug("Initializing with pid (%d) and pip (%#llX) from: %s", processId, pip->GetPipId(), __FUNCTION__);
    
    pip_ = pip;
    id_  = processId;
    bzero(path_, sizeof(path_));
    pathLength_ = 0;
}

SandboxedProcess::~SandboxedProcess()
{
    log("Releasing process object %d (%#llX) - freed from %{public}s", id_, pip_->GetPipId(),  __FUNCTION__);
    if (pip_ != nullptr)
    {
        pip_.reset();
    }
}
