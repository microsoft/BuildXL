// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "bpf/bpf.h"
#include "bpf/libbpf.h"
#include "bpf/btf.h"
#include "bpf/libbpf_common.h"
#include <cstdint>
#include <unistd.h>

#pragma once

static inline __u64 ptr_to_u64(const void *ptr)
{
	return (__u64)(unsigned long)ptr;
}

/** Retrieves the program full name of a given bpf_prog_info */
void GetProgramFullName(const struct bpf_prog_info *prog_info, int prog_fd, char *name_buff, size_t buff_len)
{
    const char *prog_name = prog_info->name;
    const struct btf_type *func_type;
    struct bpf_func_info finfo = {};
    struct bpf_prog_info info = {};
    __u32 info_len = sizeof(info);
    struct btf *prog_btf = NULL;

    // If the name is 16 chars or left, it is already contained in the info object
    if (buff_len <= BPF_OBJ_NAME_LEN || strlen(prog_info->name) < BPF_OBJ_NAME_LEN - 1) {
        goto copy_name;
    }

    if (!prog_info->btf_id || prog_info->nr_func_info == 0) {
        goto copy_name;
    }

    info.nr_func_info = 1;
    info.func_info_rec_size = prog_info->func_info_rec_size;
    if (info.func_info_rec_size > sizeof(finfo)) {
        info.func_info_rec_size = sizeof(finfo);
    }
    info.func_info = ptr_to_u64(&finfo);

    // Retrieve full info of the program
    if (bpf_prog_get_info_by_fd(prog_fd, &info, &info_len)) {
        goto copy_name;
    }

    // Load corresponding BTF object
    prog_btf = btf__load_from_kernel_by_id(info.btf_id);
    if (!prog_btf) {
        goto copy_name;
    }

    // Retrieve the function associated to the program and get the name
    func_type = btf__type_by_id(prog_btf, finfo.type_id);
    if (!func_type || !btf_is_func(func_type)) {
        goto copy_name;
    }

    prog_name = btf__name_by_offset(prog_btf, func_type->name_off);

    copy_name:
    snprintf(name_buff, buff_len, "%s", prog_name);

    if (prog_btf) {
        btf__free(prog_btf);
    }
}

/**
 * Retrieves the file descriptor of a BPF program by its name.
 */
int GetTestProgramFd(const char* program_name)
{
     __u32 id = 0;
    int err, fd = 0;
    char prog_name[128];

    // Iterate over all bpf programs
    while (true) {
        err = bpf_prog_get_next_id(id, &id);
        if (err) {
            break;
        }

        fd = bpf_prog_get_fd_by_id(id);
        if (fd < 0) {
            continue;
        }

        // We got a program with a valid file descriptor, retrieve its info
        struct bpf_prog_info info = {};
        __u32 len = sizeof(info);

        err = bpf_obj_get_info_by_fd(fd, &info, &len);
        if (err || !info.name)
        {
            continue;
        }
        // Check whether we find a program that is our loading witness
        // (this is just an arbitrarily picked program among all the ones we load)
        GetProgramFullName(&info, fd, prog_name, sizeof(prog_name));

        if (strcmp(prog_name, program_name) == 0) {
            return fd;
        }

        close(fd);
	}

    return -1;
}