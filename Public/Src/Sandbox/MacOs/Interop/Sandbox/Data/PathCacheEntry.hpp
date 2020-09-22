// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef PathCacheEntry_h
#define PathCacheEntry_h

#include <fcntl.h>
#include <libproc.h>

typedef struct {
    char data[PATH_MAX] = { '\0' };
    size_t length;
} Buffer;

struct PathCacheEntry final
{
private:
    Buffer buffer_;
public:
    PathCacheEntry(const char *path, size_t length)
    {
        memcpy(buffer_.data, path, length);
        buffer_.length = length;
    }

    PathCacheEntry(int identifier, bool isPid = false)
    {
        assert(identifier > 0);

        if (!isPid)
        {
            assert(fcntl(identifier, F_GETPATH, buffer_.data) != -1);
        }
        else
        {
            assert(proc_pidpath(identifier, (void *)buffer_.data, PATH_MAX) > 0);
        }

        buffer_.length = strlen(buffer_.data);
    }

    ~PathCacheEntry() = default;

    inline const char* GetPath() const { return buffer_.data; }
    inline const size_t GetPathLength() const { return buffer_.length; }
};

#endif /* PathCacheEntry_h */
