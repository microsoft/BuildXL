// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "IOBuffer.hpp"
#include "BuildXLSandboxShared.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(IOBuffer, OSObject)

IOBuffer* IOBuffer::create(size_t size)
{
    IOBuffer *instance = new IOBuffer;
    if (instance)
    {
        bool initialized = instance->init(size);
        if (!initialized)
        {
            instance->release();
            instance = nullptr;
        }
    }

    return instance;
}

bool IOBuffer::init(size_t size)
{
    if (!super::init())
    {
        return false;
    }

    size_   = size;
    buffer_ = IONew(char, size);

    return buffer_ != nullptr;
}

void IOBuffer::free()
{
    if (buffer_ != nullptr)
    {
        IODelete(buffer_, char, size_);
    }

    size_   = 0;
    buffer_ = nullptr;

    super::free();
}
