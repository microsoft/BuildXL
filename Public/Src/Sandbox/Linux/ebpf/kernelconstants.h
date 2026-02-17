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
#define	EPERM		 1	/* Operation not permitted */
#define	ENOENT		 2	/* No such file or directory */
#define	ECHILD		10	/* No child processes */

// Copied from include/uapi/asm-generic/errno.h
#define	ESTALE		116	/* Stale file handle */

// Copied from fcntl.h
#define O_ACCMODE   00000003
#define O_RDONLY    00000000
#define O_WRONLY    00000001
#define O_RDWR      00000002
#ifndef O_CREAT
#define O_CREAT     00000100	/* not fcntl */
#endif
#ifndef O_EXCL
#define O_EXCL      00000200	/* not fcntl */
#endif
#ifndef O_NOCTTY
#define O_NOCTTY    00000400	/* not fcntl */
#endif
#ifndef O_TRUNC
#define O_TRUNC     00001000	/* not fcntl */
#endif
#ifndef O_APPEND
#define O_APPEND    00002000
#endif
#ifndef O_NONBLOCK
#define O_NONBLOCK  00004000
#endif
#ifndef O_DSYNC
#define O_DSYNC     00010000    /* used to be O_SYNC, see below */
#endif
#ifndef FASYNC
#define FASYNC      00020000    /* fcntl, for BSD compatibility */
#endif
#ifndef O_DIRECT
#define O_DIRECT    00040000    /* direct disk access hint */
#endif
#ifndef O_LARGEFILE
#define O_LARGEFILE	00100000
#endif
#ifndef O_DIRECTORY
#define O_DIRECTORY	00200000    /* must be a directory */
#endif
#ifndef O_NOFOLLOW
#define O_NOFOLLOW  00400000    /* don't follow links */
#endif
#ifndef O_NOATIME
#define O_NOATIME   01000000
#endif
#ifndef O_CLOEXEC
#define O_CLOEXEC   02000000    /* set close_on_exec */
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
#define S_IFLNK  0120000
#define S_IFREG  0100000
#define S_IFBLK  0060000
#define S_IFDIR  0040000
#define S_IFCHR  0020000
#define S_IFIFO  0010000
#define S_ISUID  0004000
#define S_ISGID  0002000
#define S_ISVTX  0001000

#define S_ISLNK(m)  (((m) & S_IFMT) == S_IFLNK)
#define S_ISREG(m)  (((m) & S_IFMT) == S_IFREG)
#define S_ISDIR(m)  (((m) & S_IFMT) == S_IFDIR)
#define S_ISCHR(m)  (((m) & S_IFMT) == S_IFCHR)
#define S_ISBLK(m)  (((m) & S_IFMT) == S_IFBLK)
#define S_ISFIFO(m) (((m) & S_IFMT) == S_IFIFO)
#define S_ISSOCK(m) (((m) & S_IFMT) == S_IFSOCK)

// Copied from err.h
#define MAX_ERRNO   4095
#define IS_ERR_VALUE(x) x >= (unsigned long)-MAX_ERRNO

__attribute__((always_inline)) static inline bool IS_ERR(const void *ptr)
{
    return IS_ERR_VALUE((unsigned long)ptr);
}

__attribute__((always_inline)) static inline long PTR_ERR(const void *ptr)
{
    return (long) ptr;
}

// Copied from pid_max.c
#define CLONE_THREAD    0x00010000  /* Same thread group? */

// Copied from pid_namespace.h
#define MAX_PID_NS_LEVEL 32

// Copied from const.h: https://github.com/torvalds/linux/blob/master/include/uapi/linux/const.h
#define __AC(X,Y)   (X##Y)
#define _AC(X,Y)    __AC(X,Y)
#define _UL(x)      (_AC(x, UL))

// Copied from const.h: https://github.com/torvalds/linux/blob/master/include/vdso/const.h
#define UL(x)      (_UL(x))

// Copied from bits.h: https://github.com/torvalds/linux/blob/master/include/vdso/bits.h#L7
#define BIT(nr)       (UL(1) << (nr))

// Copied from namei.h: https://github.com/torvalds/linux/blob/master/include/linux/namei.h#L21
#define LOOKUP_FOLLOW		BIT(0)	/* follow links at the end */

// Copied from dcache.h: https://github.com/torvalds/linux/blob/02adc1490e6d8681cc81057ed86d123d0240909b/include/linux/dcache.h#L177
enum dentry_flags {
    DCACHE_OP_HASH          = BIT(0),
    DCACHE_OP_COMPARE       = BIT(1),
    DCACHE_OP_REVALIDATE    = BIT(2),
    DCACHE_OP_DELETE        = BIT(3),
    DCACHE_OP_PRUNE         = BIT(4),
    /*
     * This dentry is possibly not currently connected to the dcache tree,
     * in which case its parent will either be itself, or will have this
     * flag as well.  nfsd will not use a dentry with this bit set, but will
     * first endeavour to clear the bit either by discovering that it is
     * connected, or by performing lookup operations.  Any filesystem which
     * supports nfsd_operations MUST have a lookup function which, if it
     * finds a directory inode with a DCACHE_DISCONNECTED dentry, will
     * d_move that dentry into place and return that dentry rather than the
     * passed one, typically using d_splice_alias.
     */
    DCACHE_DISCONNECTED         = BIT(5),
    DCACHE_REFERENCED           = BIT(6),      /* Recently used, don't discard. */
    DCACHE_DONTCACHE            = BIT(7),      /* Purge from memory on final dput() */
    DCACHE_CANT_MOUNT           = BIT(8),
    DCACHE_GENOCIDE             = BIT(9),
    DCACHE_SHRINK_LIST          = BIT(10),
    DCACHE_OP_WEAK_REVALIDATE   = BIT(11),
    /*
     * this dentry has been "silly renamed" and has to be deleted on the
     * last dput()
     */
    DCACHE_NFSFS_RENAMED            = BIT(12),
    DCACHE_FSNOTIFY_PARENT_WATCHED  = BIT(13),      /* Parent inode is watched by some fsnotify listener */
    DCACHE_DENTRY_KILLED            = BIT(14),
    DCACHE_MOUNTED                  = BIT(15),      /* is a mountpoint */
    DCACHE_NEED_AUTOMOUNT           = BIT(16),      /* handle automount on this dir */
    DCACHE_MANAGE_TRANSIT           = BIT(17),      /* manage transit from this dirent */
    DCACHE_LRU_LIST                 = BIT(18),
    DCACHE_ENTRY_TYPE               = (7 << 19),    /* bits 19..21 are for storing type: */
    DCACHE_MISS_TYPE                = (0 << 19),    /* Negative dentry */
    DCACHE_WHITEOUT_TYPE            = (1 << 19),    /* Whiteout dentry (stop pathwalk) */
    DCACHE_DIRECTORY_TYPE           = (2 << 19),    /* Normal directory */
    DCACHE_AUTODIR_TYPE             = (3 << 19),    /* Lookupless directory (presumed automount) */
    DCACHE_REGULAR_TYPE             = (4 << 19),    /* Regular file type */
    DCACHE_SPECIAL_TYPE             = (5 << 19),    /* Other file type */
    DCACHE_SYMLINK_TYPE             = (6 << 19),    /* Symlink */
    DCACHE_NOKEY_NAME               = BIT(22),  /* Encrypted name encoded without key */
    DCACHE_OP_REAL                  = BIT(23),
    DCACHE_PAR_LOOKUP               = BIT(24),  /* being looked up (with parent locked shared) */
    DCACHE_DENTRY_CURSOR            = BIT(25),
    DCACHE_NORCU                    = BIT(26),  /* No RCU delay for freeing */
};

#endif // __KERNEL_CONSTANTS_H