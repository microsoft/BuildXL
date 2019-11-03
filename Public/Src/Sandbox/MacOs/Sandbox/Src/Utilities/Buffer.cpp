// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Alloc.hpp"
#include "Buffer.hpp"
#include "BuildXLSandboxShared.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(Buffer, OSObject)

Buffer* Buffer::create(size_t size)
{
    Buffer *instance = new Buffer;
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

bool Buffer::init(size_t size)
{
    if (!super::init())
    {
        return false;
    }

    size_   = size;
    buffer_ = Alloc::New<char>(size);

    return buffer_ != nullptr;
}

void Buffer::free()
{
    if (buffer_ != nullptr)
    {
        Alloc::Delete<char>(buffer_, size_);
    }

    size_   = 0;
    buffer_ = nullptr;

    super::free();
}
