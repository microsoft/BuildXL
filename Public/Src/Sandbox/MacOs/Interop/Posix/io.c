// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <fcntl.h>
#include <string.h>
#include <sys/errno.h>
#include <sys/mount.h>
#include <sys/stat.h>
#include <unistd.h>

#include "io.h"

static int CallStat(const char *path, bool followSymlink, struct stat *result)
{
    int ret;
    if (followSymlink)
    {
        while((ret = stat(path, result)) < 0 && errno == EINTR);
    }
    else 
    {
        ret = lstat(path, result);
    }
    
    return ret;
}

static void ConvertStatToStatBuffer(struct stat *fileStat, StatBuffer *statBuffer)
{
    statBuffer->st_dev                = (int64_t)fileStat->st_dev;
    statBuffer->st_ino                = (int64_t)fileStat->st_ino;
    statBuffer->st_mode               = (int32_t)fileStat->st_mode;
    statBuffer->st_nlink              = fileStat->st_nlink;
    statBuffer->st_uid                = fileStat->st_uid;
    statBuffer->st_gid                = fileStat->st_gid;
    statBuffer->st_size               = fileStat->st_size;
    statBuffer->st_atimespec          = fileStat->st_atime;
    statBuffer->st_atimespec_nsec     = ((fileStat)->st_atimespec.tv_nsec);
    statBuffer->st_mtimespec          = fileStat->st_mtime;
    statBuffer->st_mtimespec_nsec     = ((fileStat)->st_mtimespec.tv_nsec);
    statBuffer->st_ctimespec          = fileStat->st_ctime;
    statBuffer->st_ctimespec_nsec     = ((fileStat)->st_ctimespec.tv_nsec);
    statBuffer->st_birthtimespec      = fileStat->st_birthtimespec.tv_sec;
    statBuffer->st_birthtimespec_nsec = fileStat->st_birthtimespec.tv_nsec;
}

inline static int ToFileDescriptorUnchecked(intptr_t fd)
{
    return (int)fd;
}

int StatFile(const char *path, bool followSymlink, StatBuffer *statBuffer, long bufferSize)
{
    if (sizeof(StatBuffer) != bufferSize)
    {
        printf("ERROR: Wrong size of StatBuffer buffer; expected %ld, received %ld\n", sizeof(StatBuffer), bufferSize);
        return RUNTIME_ERROR;
    }

    struct stat fileStat;
    int result = CallStat(path, followSymlink, &fileStat);
    
    if (result == 0)
    {
        ConvertStatToStatBuffer(&fileStat, statBuffer);
    }

    return result;
}

int StatFileDescriptor(intptr_t fd, StatBuffer *statBuffer, long bufferSize)
{
    if (sizeof(StatBuffer) != bufferSize)
    {
        printf("ERROR: Wrong size of StatBuffer buffer; expected %ld, received %ld\n", sizeof(StatBuffer), bufferSize);
        return RUNTIME_ERROR;
    }

    struct stat fileStat;
    int result;
    while ((result = fstat(ToFileDescriptorUnchecked(fd), &fileStat)) < 0 && errno == EINTR);

    if (result == 0)
    {
        ConvertStatToStatBuffer(&fileStat, statBuffer);
    }

    return result;
}

intptr_t Open(const char *path, int32_t flags, int32_t mode)
{
    int result;
    while ((result = open(path, flags, (mode_t)mode)) < 0 && errno == EINTR);
    return result;
}

int SetAttributeList(const char *path, uint commonAttr, struct timespec spec, bool followSymLink)
{
    struct attrlist attributes = {0};
    attributes.bitmapcount = ATTR_BIT_MAP_COUNT;
    attributes.commonattr = commonAttr;

    return setattrlist(
        path, &attributes, (void*)&spec, sizeof(struct timespec), followSymLink ? 0 : FSOPT_NOFOLLOW);
}

int SetTimeStampsForFilePath(const char *path, bool followSymlink, StatBuffer buffer)
{
    struct timespec birthTime;
    birthTime.tv_sec = buffer.st_birthtimespec;
    birthTime.tv_nsec = buffer.st_birthtimespec_nsec;
    
    struct timespec mTime;
    mTime.tv_sec = buffer.st_mtimespec;
    mTime.tv_nsec = buffer.st_mtimespec_nsec;
    
    struct timespec aTime;
    aTime.tv_sec = buffer.st_atimespec;
    aTime.tv_nsec = buffer.st_atimespec_nsec;
    
    struct timespec cTime;
    cTime.tv_sec = buffer.st_ctimespec;
    cTime.tv_nsec = buffer.st_ctimespec_nsec;
    
    int result = SetAttributeList(path, ATTR_CMN_CRTIME, birthTime, followSymlink);
    result += SetAttributeList(path, ATTR_CMN_MODTIME, mTime, followSymlink);
    result += SetAttributeList(path, ATTR_CMN_ACCTIME, aTime, followSymlink);
    result += SetAttributeList(path, ATTR_CMN_CHGTIME, cTime, followSymlink);

    return result;
}

ssize_t SafeReadLink(const char *path, char *buffer, size_t bufferSize)
{
    if (buffer == NULL)
    {
        return STD_ERROR_CODE;
    }

    ssize_t read = readlink(path, buffer, bufferSize);
    if (read >= 0 && read < bufferSize)
    {
        buffer[read] = '\0';
        return read;
    }

    return STD_ERROR_CODE;
}

int SetFilePermissionsForFilePath(const char *path, mode_t permissions, bool followSymlink)
{
    if (path == NULL)
    {
        return STD_ERROR_CODE;
    }

    /* If path is relative and the dirfd parameter of fchmodat is the special value AT_FDCWD, then
     * path is interpreted relative to the current working directory of the calling process, like chmod() behavior
     */
    return followSymlink ? chmod(path, permissions) : fchmodat(AT_FDCWD, path, permissions, AT_SYMLINK_NOFOLLOW);
}

int GetFilePermissionsForFilePath(const char *path, bool followSymlink)
{
    if (path == NULL)
    {
        return STD_ERROR_CODE;
    }

    struct stat fileStat;
    int errorCode = CallStat(path, followSymlink, &fileStat);
    return errorCode == 0 ? fileStat.st_mode : STD_ERROR_CODE;
}

int GetFileSystemType(intptr_t fd, char *fsTypeNameBuffer, size_t bufferSize)
{
    if (fsTypeNameBuffer == NULL || bufferSize <= 0) 
    {
        return -1;
    }

    struct statfs statbuf;
    int result = fstatfs(ToFileDescriptorUnchecked(fd), &statbuf);

    if (result == 0) 
    {
        size_t requiredLength = strlen(statbuf.f_fstypename) + 1;
        if (bufferSize < requiredLength)
        {
            return -1;
        } 

        strncpy(fsTypeNameBuffer, statbuf.f_fstypename, requiredLength);
    }

    return result;
}
