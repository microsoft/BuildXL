// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __EBPF_UTILITIES_H
#define __EBPF_UTILITIES_H

#include "vmlinux.h"
#include "kernelconstants.h"

/*
 * Dictionary containing currently active process ids. Root process id is pre-populated by the userspace.
 * Observe these pids are the ones corresponding to the root namespace. So the assumption is that
 * BuildXL is running in the root namespace, otherwise pids won't match.
 * TODO: Ideally we should always return pids corresponding with the same namespace where BuildXL was launched
 * (which in an arbitrary situation might not be the root one)
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    __type(key, pid_t);
    __type(value, int);
    __uint(max_entries, 512);
} pid_map SEC(".maps");


// Whether the given pid is one we care about (i.e. is part of the pid map we keep)
__attribute__((always_inline)) static int is_valid_pid(pid_t pid) {
    pid_t *elem = bpf_map_lookup_elem(&pid_map, &pid);
    return elem ? *elem == pid : 0;
}

// Returns the parent pid of the current task
__attribute__((always_inline)) static int get_ppid() {
    struct task_struct *current = (struct task_struct*)bpf_get_current_task();
    struct task_struct *parent;
    int ppid;

    bpf_core_read(&parent, sizeof(parent), &current->real_parent);
    bpf_core_read(&ppid, sizeof(ppid), &parent->tgid);

    return ppid;
}

// We use one entry per cpu
// Used to store temporary paths
struct
{
    __uint(type, BPF_MAP_TYPE_ARRAY);
    __uint(key_size, sizeof(uint32_t));
    __uint(value_size, PATH_MAX);
    __uint(max_entries, MAX_PROC);
} tmp_paths SEC(".maps");

// We use one entry per cpu
// Used by deref_path_info and combine_paths
// The dereference needs two paths, so the size here is PATH_MAX * 2, and the resulting element is logically split in halves
// More generally, it is very useful to use a PATH_MAX * 2 sized buffer for path-related operations (when two paths are involved, or when 
// a temporary path is kept while operating with another one), as the verifier will be happy with the given boundaries
struct
{
    __uint(type, BPF_MAP_TYPE_ARRAY);
    __uint(key_size, sizeof(uint32_t));
    __uint(value_size, PATH_MAX * 2);
    __uint(max_entries, MAX_PROC);
} derefpaths SEC(".maps");

// Returns a string representation of the content of a struct path (dentry and vfsmount being its two components)
__attribute__((always_inline)) static inline uint32_t deref_path_info(char *dest, const void *dentry, const void *vfsmount)
{
    int dlen, dlen2;
    char *dname = NULL;
    char *temp = NULL;
    unsigned int i;
    unsigned int size = 0;
    uint32_t map_id = bpf_get_smp_processor_id();
    const void *path = NULL;
    const void *newdentry = NULL;
    const void *mnt = NULL;
    uint32_t tsize = 0;

    // nullify string in case of error
    dest[0] = 0x00;
 
    mnt = container_of(vfsmount, struct mount, mnt);
 
    // retrieve temporary filepath storage
    temp = bpf_map_lookup_elem(&derefpaths, &map_id);
    if (!temp)
    {
        return 0;
    }
 
    for (i = 0; i < FILEPATH_NUMDIRS; i++)
    {
        dname = (char *)BPF_CORE_READ((struct dentry *)dentry, d_name.name);
       
        if (!dname)
        {
            // If we didn't have a mount set, this means we reach the root of the filesystem
            if (vfsmount == NULL)
            {
                break;
            }

            return 0;
        }
        // store this dentry name in start of second half of our temporary storage
        dlen = bpf_core_read_str(&temp[PATH_MAX], PATH_MAX, dname);
 
        // get parent dentry
        newdentry = (char *)BPF_CORE_READ((struct dentry *)dentry, d_parent);
        
        // Check if the retrieved dname is just a '/'. In that case, we just want to skip it.
        // We will consistently add separators in between afterwards, so we don't want a double slash
        if (!(temp[PATH_MAX] == '/' && dlen == 2))
        {
            
            // copy the temporary copy to the first half of our temporary storage, building it backwards from the middle of
            // it
            dlen2 = bpf_core_read_str(&temp[(PATH_MAX - size - dlen) & (PATH_MAX - 1)], dlen & (PATH_MAX - 1), &temp[PATH_MAX]);
            // check if current dentry name is valid
            if (dlen2 <= 0 || dlen <= 0 || dlen >= PATH_MAX || size + dlen > PATH_MAX)
            {
                return 0;
            }

            if (size > 0)
            {
                asm volatile("%[tsize] = " XSTR(PATH_MAX) "\n"
                            "%[tsize] -= %[size]\n"
                            "%[tsize] -= 1\n"
                            "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                            : [size] "+&r"(size), [tsize] "+&r"(tsize)
                            );
    
                temp[tsize & (PATH_MAX - 1)] = '/';
            }

            size = (size + dlen2) &
                (PATH_MAX - 1);  // by restricting size to PATH_MAX we help the verifier keep the complexity
                                    // low enough so that it can analyse the loop without hitting the 1M ceiling
        }

        // check if this is the root of the filesystem or we reach the given mountpoint
        // We always prefer the mountpoint instead of continuing walking up the chain so we honor what the application context
        // is trying to do wrt path lookups
        if (!newdentry || dentry == newdentry || newdentry == BPF_CORE_READ((struct vfsmount *)vfsmount, mnt_root))
        {
            // check if we're on a mounted partition
            // find mount struct from vfsmount
            const void *parent = BPF_CORE_READ((struct mount *)mnt, mnt_parent);
 
            // check if we're at the real root
            if (parent == mnt)
            {
                break;
            }

            // move to mount point
            newdentry = BPF_CORE_READ((struct mount *)mnt, mnt_mountpoint);
            mnt = parent;
 
            // another check for real root
            if (dentry == newdentry)
            {
                break;
            }
        }

        // go up one directory
        dentry = newdentry;
    }
 
    // check if we exhausted the number of directories we can traverse
    if (i == FILEPATH_NUMDIRS)
    {
        // add a '+/' to the start to indicate it's not a full path
 
        // Following piece of asm is required as clang likes to optimise
        // an increment followed by ANDing with (PATH_MAX -1), into simply
        // XORing with (PATH_MAX -1) and then converting to 32 bits by
        // <<32, >>32. This means the verifier thinks max value is 2^32 -1,
        // instead of (PATH_MAX -1).
 
        asm volatile("%[size] += 1\n"
                     "%[tsize] = " XSTR(PATH_MAX) "\n"
                     "%[tsize] -= %[size]\n"
                     "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                    : [size] "+&r"(size), [tsize] "+&r"(tsize)
                    );
        temp[tsize & (PATH_MAX - 1)] = '/';
 
        asm volatile("%[size] += 1\n"
                     "%[tsize] -= 1\n"
                     "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                    : [size] "+&r"(size), [tsize] "+&r"(tsize)
                    );
        temp[tsize & (PATH_MAX - 1)] = '+';
    }
    else if (size == 1)
    {
        // smallest size is 1 as a 0 length read above would have bailed
        // so the shortest valid read would be a single null character.
        // assume this represents the root dir
        size++;
        temp[(PATH_MAX - size) & (PATH_MAX -1)] = '/';
    }
    else if (size > 2)
    {
        // size of 2 is simply "/" which is good. Need to check >2.
 
        // check if starting with '/'
        if (temp[(PATH_MAX - size) & (PATH_MAX - 1)] == '/')
        {
            // check for double / ("//")
            if (temp[(PATH_MAX - (size - 1)) & (PATH_MAX - 1)] == '/')
            {
                size--;
            }
        }
        else
        {
            // add a '/'
 
            asm volatile("%[size] += 1\n"
                         "%[tsize] = " XSTR(PATH_MAX) "\n"
                         "%[tsize] -= %[size]\n"
                         "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                        : [size] "+&r"(size), [tsize] "+&r"(tsize)
                        );
 
            temp[tsize & (PATH_MAX - 1)] = '/';
        }
    }
 
    // copy the path from the temporary location to the destination
    char *msg = &temp[(PATH_MAX - size) & (PATH_MAX -1)];
    dlen = bpf_core_read_str(dest, PATH_MAX, &temp[(PATH_MAX - size) & (PATH_MAX -1)]);
   
    if (dlen <= 0)
    {
        return 0;
    }

    return dlen;
}

// Returns the mode stored in the corresponding inode
__attribute__((always_inline)) static inline unsigned int get_mode(const struct dentry *dentry) {
    return BPF_CORE_READ(dentry, d_inode, i_mode);
}

// Returns the mode stored in the corresponding inode
__attribute__((always_inline)) static inline unsigned int get_mode_from_file(const struct file *file) {
    return BPF_CORE_READ(file, f_inode, i_mode);
}

// Returns the mode stored in the corresponding inode
__attribute__((always_inline)) static inline unsigned int get_mode_from_path(const struct path *path) {
    return get_mode(BPF_CORE_READ(path, dentry));
}

// Turns a struct path into a string representation of the full path
__attribute__((always_inline)) static inline uint32_t path_to_string(char *dest, const struct path* path)
{
    if (!dest)
    {
        return 0;
    }

    dest[0] = '\0';

    void *dentry = BPF_CORE_READ(path, dentry);
    if (!dentry)
    {
        return 0;
    }

    // Observe de-referencing can work even if the mount is missing, so no null checking here.
    void *vfsmount = BPF_CORE_READ(path, mnt);
    
    return deref_path_info(dest, dentry, vfsmount);
}

__attribute__((always_inline)) static inline uint32_t fd_to_string(char *fdPath, int fd, const void *task)
{
    void *path = NULL;
    
    // check if fd is valid
    int maxFds = BPF_CORE_READ((struct task_struct *)task, files, fdt, max_fds);
    if (fd < 0 || fd > MAX_FDS || maxFds <= 0 || fd > maxFds) 
    {
        return 0;
    }

    // Retrieve the file descriptor table from the current task
    const void **fdTable = (const void **)BPF_CORE_READ((struct task_struct *)task, files, fdt, fd);
    if (!fdTable)
    {
        return 0;
    }

    // Retrieve the struct file instance that is pointed by the fd
    const struct file *fdfile = NULL;
    if (bpf_core_read(&fdfile, sizeof(fdfile), &fdTable[fd & MAX_FDS]) != READ_OKAY || !fdfile)
    {
        return 0;
    }
    else
    {
        // Resolve the corresponding struct path to a string
        struct path path = BPF_CORE_READ(fdfile, f_path);
        return path_to_string(fdPath, &path);
    }
}

// Combines path and atom, placing the result in path
__attribute__((always_inline)) static inline uint32_t combine_paths(char* path, const char* atom)
{
    uint32_t tsize = 0;

    char *temp_path = NULL;
    uint32_t map_id = bpf_get_smp_processor_id();    
    temp_path = bpf_map_lookup_elem(&derefpaths, &map_id);
    if (!temp_path)
    {
        return 0;
    }

    // Copy the path to the temporary path. Observe the temporary path has size 2*PATH_MAX, so the
    // verifier will allow putting two paths together without complaining
    int length = bpf_core_read_str(temp_path, PATH_MAX, path);

    // Check whether the last element of the path is already a directory separator, and add one otherwise.
    // Observe that length includes the null character, so 'length - 1' should be pointing to the null separator
    if (temp_path[(length - 2) & (PATH_MAX - 1)] != '/')
    {
        temp_path[(length - 1) & (PATH_MAX - 1)] = '/';
    }
    else
    {
        length--;
    }

    bpf_core_read_str(&temp_path[length & (PATH_MAX - 1)], PATH_MAX, atom);

    // Copy to the final destination
    return bpf_core_read_str(path, PATH_MAX, temp_path);
}

// Returns the current working directory of the given task
__attribute__((always_inline)) static inline uint32_t get_cwd(struct task_struct * task, char* cwd)
{
    struct path pwd_path = BPF_CORE_READ(task, fs, pwd);
    return path_to_string(cwd, &pwd_path);
}

// Returns a string representation of the path carried by file descriptor followed but a filename.
// These input arguments are used to perform a path lookup, which means that the dentry/inode is not resolved
// yet
__attribute__((always_inline)) static inline uint32_t fd_string_to_string(char* path, int fd, const char* filename)
{
    // Copy the filename to the destination, as a way to bound it to PATH_MAX and keep the verifier happy
    int length = bpf_core_read_str(path, PATH_MAX, filename);

    if (length <= 0)
    {
        return 0;
    }
    
    // Check if file descriptor is invalid or if the filename is absolute. In those case, the file descriptor is ignored and
    // the final path should be in the filename
    if ((fd < 0 && fd != AT_FDCWD) || path[0] == '/')
    {
        return length;
    }

    // The file descriptor is valid. This could either be AT_FDCWD (the current directory) or a valid handle
    struct task_struct *task = (struct task_struct *)bpf_get_current_task();
    if (fd == AT_FDCWD)
    {
        length = get_cwd(task, path);
    }
    else
    {
        length = fd_to_string(path, fd, task);
    }

    if (length <= 0)
    {        
        return 0;
    }

    // We got a resolved directory in path and a relative path on filename. Put them together.
    return combine_paths(path, filename);
}

// Returns a string representation of the path carried by file descriptor followed by a struct filename.
// These input arguments are used to perform a path lookup, which means that the dentry/inode is not resolved
// yet
__attribute__((always_inline)) static inline uint32_t fd_filename_to_string(char* path, int fd, const struct filename* filename_struct)
{
    const char *filename = BPF_CORE_READ(filename_struct, name);

    return fd_string_to_string(path, fd, filename);
}

// Returns a string representation of the path carried by a nameidata instance.
// Observe that nameidata is typically used to perform a path lookup, which means that the dentry/inode is not resolved
// yet (and might not even exist in case of an absent path). The consequence of this is that we have to do extra work to
// put the final path together
__attribute__((always_inline)) static inline uint32_t nameidata_to_string(char* path, const struct nameidata* ns)
{
    // A nameidata contains a file descriptor maybe pointing to a directory (dfd) and a name component which may contain a filename 
    // or full path 
    int fd = BPF_CORE_READ(ns, dfd);
    const struct filename *filename = BPF_CORE_READ(ns, name);

    return fd_filename_to_string(path, fd, filename);
}

// Returns the path to the current executable by inspecting the given task
__attribute__((always_inline)) static inline int get_task_exec_path(struct task_struct* task, char* dest)
{
    dest[0] = '\0';

    struct mm_struct* mm = BPF_CORE_READ(task, mm);
    if (mm)
    {
        struct path path = BPF_CORE_READ(mm, exe_file, f_path);
        return path_to_string(dest, &path);
    }
    
    return 0;
}

#endif // __EBPF_UTILITIES_H