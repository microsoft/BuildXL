//
//  io.c
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include <fcntl.h>
#include <unistd.h>
#include <sys/attr.h>
#include <sys/errno.h>
#include <sys/stat.h>

#include "io.h"

static int StatFile(const char *path, bool followSymlink, struct stat *result)
{
    return followSymlink ? stat(path, result) : lstat(path, result);
}

int GetTimeStampsForFilePath(const char *path, bool followSymlink, Timestamps *buffer)
{
    if (buffer == NULL)
    {
        return EIO;
    }
    
    struct stat fileStat;
    int result = StatFile(path, followSymlink, &fileStat);

    if (result == 0)
    {
        buffer->creationTime = fileStat.st_birthtimespec;
        buffer->modificationTime = fileStat.st_mtimespec;
        buffer->acessTime = fileStat.st_atimespec;
        buffer->changeTime = fileStat.st_ctimespec;
    }

    return result;
}

int GetDeviceAndInodeNumbers(const char *path, bool followSymlink, int32_t *dev, uint64_t *ino)
{
    struct stat fileStat;
    int errorCode = StatFile(path, followSymlink, &fileStat);
    if (errorCode != 0) // error
    {
        return errorCode;
    }

    *ino = fileStat.st_ino;
    *dev = fileStat.st_dev;
    return 0;
}

int SetAttributeList(const char* path, unsigned int commonAttr, struct timespec spec, bool followSymLink)
{
    struct attrlist attributes = {0};
    attributes.bitmapcount = ATTR_BIT_MAP_COUNT;
    attributes.commonattr = commonAttr;

    return setattrlist(path, &attributes, (void*)&spec, sizeof(struct timespec), followSymLink ? 0 : FSOPT_NOFOLLOW);
}

int SetTimeStampsForFilePath(const char *path, bool followSymlink, Timestamps *buffer)
{
    if (buffer == NULL)
    {
        return EIO;
    }

    int result = SetAttributeList(path, ATTR_CMN_CRTIME, buffer->creationTime, followSymlink);
    result += SetAttributeList(path, ATTR_CMN_MODTIME, buffer->modificationTime, followSymlink);
    result += SetAttributeList(path, ATTR_CMN_ACCTIME, buffer->acessTime, followSymlink);
    result += SetAttributeList(path, ATTR_CMN_CHGTIME, buffer->changeTime, followSymlink);

    return result;
}

ssize_t SafeReadLink(const char *path, char *buffer, size_t bufsiz)
{
    if (buffer == NULL)
    {
        return STD_ERROR_CODE;
    }

    ssize_t read = readlink(path, buffer, bufsiz);
    if (read >= 0 && read < bufsiz)
    {
        buffer[read] = '\0';
        return read;
    }

    return STD_ERROR_CODE;
}

int GetHardLinkCountForFilePath(const char *path, bool followSymlink)
{
    if (path == NULL)
    {
        return STD_ERROR_CODE;
    }

    struct stat fileStat;
    int result = followSymlink ? stat(path, &fileStat) : lstat(path, &fileStat);
    if (result == 0)
    {
        return fileStat.st_nlink;
    }

    return result;
}

int GetFilePermissionsForFilePath(const char *path, bool followSymlink)
{
    if (path == NULL)
    {
        return STD_ERROR_CODE;
    }

    struct stat fileStat;
    int errorCode = followSymlink ? stat(path, &fileStat) : lstat(path, &fileStat);
    return errorCode == 0 ? fileStat.st_mode : STD_ERROR_CODE;
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
