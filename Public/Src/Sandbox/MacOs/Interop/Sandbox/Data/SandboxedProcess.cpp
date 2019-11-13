// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "SandboxedProcess.hpp"
#include <execinfo.h>
#include <errno.h>

SandboxedProcess::SandboxedProcess(pid_t processId, SandboxedPip *pip)
{
    assert(pip != nullptr);
    pip_ = pip;
    id_  = processId;

    bzero(path_, sizeof(path_));
    pathLength_ = 0;

    if (pip_ == nullptr)
    {
        throw "No valid SandboxedPip provided on SandboxedProcess construction!";
    }
}

SandboxedProcess::~SandboxedProcess()
{
    log("Releasing process object %d (%#llX) - freed from %{public}s", id_, pip_->getPipId(),  __FUNCTION__);
    if (pip_ != nullptr) delete pip_;
}
