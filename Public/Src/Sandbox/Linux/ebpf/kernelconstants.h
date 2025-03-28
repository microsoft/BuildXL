// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __KERNEL_CONSTANTS_H
#define __KERNEL_CONSTANTS_H

// Redefined here to avoid including unnecessary headers in bpf code.

#define MAX_FDS 65535
#define READ_OKAY 0
#define FILEPATH_NUMDIRS 95
#define MAX_PROC 512
#define AT_FDCWD -100

#define STR(x) #x
#define XSTR(s) STR(s)

// CODESYNC: linux/limits.h
#define PATH_MAX 4096

// Copied from include/uapi/asm-generic/errno-base.h
#define	ENOENT		 2	/* No such file or directory */

// Copied from fcntl.h
#define O_ACCMODE	00000003
#define O_RDONLY	00000000
#define O_WRONLY	00000001
#define O_RDWR      00000002
#ifndef O_CREAT
#define O_CREAT     00000100	/* not fcntl */
#endif
#ifndef O_EXCL
#define O_EXCL      00000200	/* not fcntl */
#endif
#ifndef O_NOCTTY
#define O_NOCTTY	00000400	/* not fcntl */
#endif
#ifndef O_TRUNC
#define O_TRUNC     00001000	/* not fcntl */
#endif
#ifndef O_APPEND
#define O_APPEND	00002000
#endif
#ifndef O_NONBLOCK
#define O_NONBLOCK	00004000
#endif
#ifndef O_DSYNC
#define O_DSYNC     00010000	/* used to be O_SYNC, see below */
#endif
#ifndef FASYNC
#define FASYNC      00020000	/* fcntl, for BSD compatibility */
#endif
#ifndef O_DIRECT
#define O_DIRECT	00040000	/* direct disk access hint */
#endif
#ifndef O_LARGEFILE
#define O_LARGEFILE	00100000
#endif
#ifndef O_DIRECTORY
#define O_DIRECTORY	00200000	/* must be a directory */
#endif
#ifndef O_NOFOLLOW
#define O_NOFOLLOW	00400000	/* don't follow links */
#endif
#ifndef O_NOATIME
#define O_NOATIME	01000000
#endif
#ifndef O_CLOEXEC
#define O_CLOEXEC	02000000	/* set close_on_exec */
#endif

typedef unsigned int fmode_t;
#define FMODE_CREATED   ((fmode_t)(1 << 20))

// Copied from fs.h
// Mask passed to security_file_permission
#define MAY_EXEC    0x00000001
#define MAY_WRITE   0x00000002
#define MAY_READ    0x00000004
#define MAY_APPEND  0x00000008
#define MAY_ACCESS  0x00000010
#define MAY_OPEN    0x00000020
#define MAY_CHDIR   0x00000040

// Copied from stat.h
#define S_IFMT  00170000
#define S_IFSOCK 0140000
#define S_IFLNK	 0120000
#define S_IFREG  0100000
#define S_IFBLK  0060000
#define S_IFDIR  0040000
#define S_IFCHR  0020000
#define S_IFIFO  0010000
#define S_ISUID  0004000
#define S_ISGID  0002000
#define S_ISVTX  0001000

#define S_ISLNK(m)	(((m) & S_IFMT) == S_IFLNK)
#define S_ISREG(m)	(((m) & S_IFMT) == S_IFREG)
#define S_ISDIR(m)	(((m) & S_IFMT) == S_IFDIR)
#define S_ISCHR(m)	(((m) & S_IFMT) == S_IFCHR)
#define S_ISBLK(m)	(((m) & S_IFMT) == S_IFBLK)
#define S_ISFIFO(m)	(((m) & S_IFMT) == S_IFIFO)
#define S_ISSOCK(m)	(((m) & S_IFMT) == S_IFSOCK)

// Copied from err.h
#define MAX_ERRNO	4095
#define IS_ERR_VALUE(x) x >= (unsigned long)-MAX_ERRNO

static inline bool IS_ERR(const void *ptr)
{
    return IS_ERR_VALUE((unsigned long)ptr);
}

static inline long PTR_ERR(const void *ptr)
{
    return (long) ptr;
}

// Copied from pid_max.c
#define CLONE_THREAD	0x00010000	/* Same thread group? */

// Copied from pid_namespace.h
#define MAX_PID_NS_LEVEL 32

#endif // __KERNEL_CONSTANTS_H