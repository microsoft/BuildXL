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

/*!
 * Returns device and inode numbers corresponding the the file at the given location
 * @param path Location of the file
 * @param followSymlink Whether to follow (use 'stat') or not (use 'lstat') symlinks
 * @param dev Where the device id will be stored
 * @param ino Where the inode will be stored
 * @result 0 on success, error code otherwise.
 */
int GetDeviceAndInodeNumbers(const char *path, bool followSymlink, int32_t *dev, uint64_t *ino);

ssize_t SafeReadLink(const char *path, char *buffer, size_t bufsiz);
int GetHardLinkCountForFilePath(const char *path, bool followSymlink);

int GetFilePermissionsForFilePath(const char *path, bool followSymlink);
int SetFilePermissionsForFilePath(const char *path, mode_t permissions, bool followSymlink);

#endif /* io_h */

