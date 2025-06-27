// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __KERNEL_FUNCTIONS_H
#define __KERNEL_FUNCTIONS_H

#include "vmlinux.h"
#include "kernelconstants.h"

/**
 * NOTE: these functions are put into a separate header file to avoid
 * having to include vmlinux.h inside kernalconstants.h.
 * Including vmlinux.h inside kernelconstants.h would cause build issues
 * since there are some duplicated definitions in both files.
 */

// Copied from dcache.h: https://github.com/torvalds/linux/blob/master/include/linux/dcache.h#L407
static inline unsigned __d_entry_type(const struct dentry *dentry)
{
    return dentry->d_flags & DCACHE_ENTRY_TYPE;
}

// Copied from dcache.h: https://github.com/torvalds/linux/blob/master/include/linux/dcache.h#L437
static inline bool d_is_symlink(const struct dentry *dentry)
{
    return __d_entry_type(dentry) == DCACHE_SYMLINK_TYPE;
}

#endif // __KERNEL_FUNCTIONS_H