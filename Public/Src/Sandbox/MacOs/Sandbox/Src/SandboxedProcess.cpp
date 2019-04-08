// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "SandboxedProcess.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(SandboxedProcess, OSObject)

SandboxedProcess* SandboxedProcess::create(pid_t processId, SandboxedPip *pip)
{
    SandboxedProcess *instance = new SandboxedProcess;
    if (instance != nullptr)
    {
        if (!instance->init(processId, pip))
        {
            OSSafeReleaseNULL(instance);
        }
    }

    return instance;
}

bool SandboxedProcess::init(pid_t processId, SandboxedPip *pip)
{
    if (!super::init())
    {
        return false;
    }

    pip_ = pip;
    id_  = processId;

    bzero(path_, sizeof(path_));
    pathLength_ = 0;

    if (pip_ == nullptr)
    {
        return false;
    }

    pip_->retain();
    return true;
}

void SandboxedProcess::free()
{
    OSSafeReleaseNULL(pip_);
    super::free();
}
