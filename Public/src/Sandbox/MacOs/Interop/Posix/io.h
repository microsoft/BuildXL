//
//  io.h
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef io_h
#define io_h

#include <stdbool.h>
#include <sys/types.h>

#define STD_ERROR_CODE -1

typedef struct timespec spec;

typedef struct {
    spec creationTime;
    spec modificationTime;
    spec acessTime;
    spec changeTime;
} Timestamps;

int GetTimeStampsForFilePath(const char *path, bool followSymlink, Timestamps *buffer);
int SetTimeStampsForFilePath(const char *path, bool followSymlink, Timestamps *buffer);

ssize_t SafeReadLink(const char *path, char *buffer, size_t bufsiz);
int GetHardLinkCountForFilePath(const char *path, bool followSymlink);

int GetFilePermissionsForFilePath(const char *path, bool followSymlink);
int SetFilePermissionsForFilePath(const char *path, mode_t permissions, bool followSymlink);

#endif /* io_h */

