// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef io_h
#define io_h

#include "Dependencies.h"

#define STD_ERROR_CODE -1

typedef struct timespec spec;

typedef struct {
    int64_t st_dev;                  /* ID of device containing file */
    int64_t st_ino;                  /* Inode number */
    int32_t st_mode;                 /* File type and mode */
    int64_t st_nlink;                /* Number of hard links */
    int32_t st_uid;                  /* User ID of owner */
    int32_t st_gid;                  /* Group ID of owner */
    int64_t st_size;                 /* Total size, in bytes */
    int64_t st_atimespec;            /* Time of last access */
    int64_t st_atimespec_nsec;       /* Time of last access - nsec */
    int64_t st_mtimespec;            /* Time of last modification */
    int64_t st_mtimespec_nsec;       /* Time of last modification - nsec*/
    int64_t st_ctimespec;            /* Time of last status change */
    int64_t st_ctimespec_nsec;       /* Time of last status change - nsec */
    int64_t st_birthtimespec;        /* Time of birth (or creation) */
    int64_t st_birthtimespec_nsec;   /* Time of birth (or creation) - nsec */
} StatBuffer;

/*!
 * Returns information about a file specified by the given path.
 * @param path Location of the file
 * @param followSymlink Whether to follow symlink, if true, then use 'stat', otherwise use 'lstat'
 * @param statBuffer Buffer where the file information is stored
 * @param bufferSize Allocated size of the 'statBuffer' struct
 * @result 0 on success, error code otherwise.
*/
int StatFile(const char *path, bool followSymlink, StatBuffer *statBuffer, long bufferSize);

/*!
 * Returns information about a file specified by the given file descriptor.
 * @param fd File descriptor
 * @param statBuffer Buffer where the file information is stored
 * @param bufferSize Allocated size of the 'statBuffer' struct
 * @result 0 on success, error code otherwise.
*/
int StatFileDescriptor(intptr_t fd, StatBuffer *statBuffer, long bufferSize);

/*!
 * Opens file specified by path.
 * @param path Given path to open
 * @param flags Open flags
 * @param mode Open mode
 * @result Non-negative number on success.
*/
intptr_t Open(const char *path, int32_t flags, int32_t mode);

ssize_t SafeReadLink(const char *path, char *buffer, size_t bufferSize);

int SetTimeStampsForFilePath(const char *path, bool followSymlink, StatBuffer buffer);

int GetFilePermissionsForFilePath(const char *path, bool followSymlink);
int SetFilePermissionsForFilePath(const char *path, mode_t permissions, bool followSymlink);

int GetFileSystemType(intptr_t fd, char *fsTypeNameBuffer, size_t bufferSize);

#endif /* io_h */
