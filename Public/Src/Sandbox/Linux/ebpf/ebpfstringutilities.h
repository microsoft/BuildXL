// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFSTRINGUTILITIES_H
#define __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFSTRINGUTILITIES_H

#include "vmlinux.h"
#include "kernelconstants.h"
#include "percpustack.h"

/**
 * Context used for string operations.
 */
struct string_contains_context {
    char *needle;
    int needle_len;
    char *haystack;
    int haystack_len;
    int matched_len;        // Current matching progress.
    int result;             // Set to 1 if match found.
    bool case_sensitive;
};

/** 
 * string_contains_callback() - Callback invoked for each character in the haystack.
 */
static long string_contains_callback(u64 index, struct string_contains_context *ctx) {
    char ch = ctx->haystack[index & (PATH_MAX - 1)];

    // If end of haystack reached, break the loop.
    if (ch == '\0') {
        return 1;
    }

    char haystack_char = ch;
    char needle_char = ctx->needle[ctx->matched_len & (PATH_MAX - 1)];
    if (!ctx->case_sensitive) {
        haystack_char = (haystack_char >= 'A' && haystack_char <= 'Z') ? haystack_char + ('a' - 'A') : haystack_char;
        needle_char = (needle_char >= 'A' && needle_char <= 'Z') ? needle_char + ('a' - 'A') : needle_char;
    }

    if (haystack_char == needle_char) {
        ctx->matched_len++;
        if (ctx->matched_len == ctx->needle_len) {
            ctx->result = 1; // Full needle found in haystack.
            return 1;        // Break loop.
        }
    }
    else {
        // If the remaining haystack length is less than the needle length,
        // we can stop searching.
        if (ctx->haystack_len - index < ctx->needle_len) {
            return 1; // Break loop.
        }

        // Reset progress; if current char equals the first character, start progress.
        if (ctx->case_sensitive) {
            ctx->matched_len = (ch == ctx->needle[0]) ? 1 : 0;
        } else {
            char first_needle = (ctx->needle[0] >= 'A' && ctx->needle[0] <= 'Z') ? ctx->needle[0] + ('a' - 'A') : ctx->needle[0];
            char current_char = (ch >= 'A' && ch <= 'Z') ? ch + ('a' - 'A') : ch;
            ctx->matched_len = (current_char == first_needle) ? 1 : 0;
        }
    }

    return 0;
}

/**
 * string_contains() - Check if 'needle' is contained in 'haystack'.
 * @needle: The string to search for.
 * @needle_len: Length of the needle string.
 * @haystack: The string to search in.
 * @haystack_len: Length of the haystack string.
 * @caseInsensitive: Whether the search should be case insensitive.
 * 
 * The search loop body is run via the callback.
 * All strings passed to this function should be size `PATH_MAX`.
 */
bool string_contains(char *needle, int needle_len, char *haystack, int haystack_len, bool case_sensitive) {
    if (!needle
        || !haystack
        || needle_len <= 0
        || haystack_len <= 0
        || needle_len > haystack_len) {
        return false;
    }

    struct string_contains_context ctx = {
        .needle = needle,
        .haystack = haystack,
        .needle_len = needle_len,
        .haystack_len = haystack_len,
        .matched_len = 0,
        .result = 0,
        .case_sensitive = case_sensitive
    };

    // Loop over the haystack bounded by PATH_MAX.
    bpf_loop(haystack_len, string_contains_callback, &ctx, 0);
    return (ctx.result == 1);
}

/**
 * Context used for nullifying a string.
 */
struct nullify_string_context {
    char *str;
    int buffer_len;
};

/**
 * nullify_string_callback() - Callback to nullify a string character by character.
 * @index: The current index in the string.
 * @ctx: The context containing the string to nullify.
 *
 * This function sets the character at the current index to null.
 */
__attribute__((always_inline)) static long nullify_string_callback(u64 index, struct nullify_string_context *ctx) {
    // Set the character at the current index to null.
    ctx->str[index & (ctx->buffer_len - 1)] = '\0';
    return 0; // Continue looping.
}

/**
 * nullify_string() - Nullifies a string by setting all characters to null.
 * Goes beyond the first null character to ensure the entire buffer is cleared.
 * @str: The string to nullify.
 * @num_chars: The number of characters to nullify.
 * @power_of_2_buffer_len: The max length of the buffer holding the string. Must be a power of 2.
 */
__attribute__((always_inline)) static void nullify_string(char *str, int num_chars, int power_of_2_buffer_len) {
    if (!str || power_of_2_buffer_len <= 0 || num_chars <= 0 || num_chars > power_of_2_buffer_len) {
        return;
    }

    struct nullify_string_context ctx = {
        .str = str,
        .buffer_len = power_of_2_buffer_len
    };

    bpf_loop(num_chars, nullify_string_callback, &ctx, 0);
}

/**
 * An instruction to shift the content of a string to the left starting from a given index.
 * This is used to remove redundant path components like '.' and '..'.
 */
struct shift_instruction {
    // The starting index of the shift operation.
    int start_index;
    // The number of positions to shift left.
    int shift;
};

/**
 * Per-CPU array of shift instructions.
 * build_shift_instructions() populates this map with the instructions needed to canonicalize a path.
 * canonicalize_path() then consumes these instructions to modify the path in place.
 */
struct shift_instructions
{
    __uint(type, BPF_MAP_TYPE_PERCPU_ARRAY);
    __uint(key_size, sizeof(int));
    __uint(value_size, sizeof(struct shift_instruction));
    __uint(max_entries, PATH_MAX);
} shift_instructions SEC(".maps");

/**
 * Context for shifting a string to the left.
 */
struct shift_left_context {
    // The string to shift.
    char *str;
    // The length of the string.
    int str_len;
    // The number of positions to shift left.
    int shift;
    // Current index in the string.
    int start_index;
};

/** shift_left_callback() - Callback to shift a string left. */
__attribute__((always_inline)) static long shift_left_callback(u64 index, struct shift_left_context *ctx) {
    int the_index = ctx->start_index + index;
    // This should never be the case, but let's keep the verifier happy
    if (the_index >= ctx->str_len) {
        return 1;
    }

    // Move str[the_index] to str[the_index - shift]
    char current_char = ctx->str[the_index & (PATH_MAX - 1)];
    int the_target_index = (the_index - ctx->shift) & (PATH_MAX - 1);
    ctx->str[the_target_index] = current_char;

    // Stop the loop if we reach the end of the string.
    if (current_char == '\0') {
        return 1;
    }

    return 0;
}

/** 
 * shift_left() - Shifts a string to the left starting from a given index.
 * @str: The string to shift.
 * @str_len: The length of the string.
 * @start_index: The index to start shifting from.
 * @shift: The number of positions to shift left.
 */
__attribute__((always_inline)) static void shift_left(char *str, int str_len, int start_index, int shift) {
    if (!str || str_len <= 0 || shift <= 0 || str_len > PATH_MAX || start_index <= 0 || start_index >= str_len || start_index - shift < 0) {
        return;
    }

    struct shift_left_context ctx = {
        .str = str,
        .str_len = str_len,
        .shift = shift,
        .start_index = start_index
    };

    // We shift from the start index to the end of the string.
    bpf_loop(str_len - start_index, shift_left_callback, &ctx, 0);
}

/** Context for building shift instructions. */
struct build_shift_instructions_context {
    // The path to analyze.
    const char *path;
    // The length of the path.
    int path_len;
    // The number of shift instructions generated.
    int shift_instructions_len;
    // The current shift amount. We want to compute instructions taking into consideration previous shifts, so they
    // can be directly applied one after the other.
    int current_shift;
};

/** Check if the current character is a slash or the end of the string. */
__attribute__((always_inline)) static bool slash_or_end(const char* path, int index) {
    char ch = path[index & (PATH_MAX - 1)];
    return ch == '/' || ch == '\0';
}

/** 
 * Build shift instructions callback 
 * The shift_instructions array is populated with the instructions to canonicalize the path. Not part of the context
 * to avoid verifier issues.
 * Uses the implicit index stack (see percpustack.h) to keep track of the indices of slashes in the path. Not part of
 * the context, same reasons as above
 * */
__attribute__((always_inline)) static int build_shift_instructions_callback(u64 index, struct build_shift_instructions_context *ctx) {
    // This should never happen as the string is null-terminated, but let's keep the verifier happy
    if (index >= ctx->path_len) {
        // End of path reached, stop the loop.
        return 1;
    }
    
    char current_char = ctx->path[index & (PATH_MAX - 1)];

    // End of path reached, stop the loop.
    if (current_char == '\0') {
        return 1; 
    }

    struct shift_instruction shift = {0};

    // Let's analyze the cases that need canonicalization. Since we want the shift instructions to be directly
    // applicable one after the other, we need to take into consideration the current shift amount.
    if (current_char == '/') {
        int index_to_push = index - ctx->current_shift;
        // Case of consecutive slashes '//'
        if (index < ctx->path_len - 1 && ctx->path[(index + 1) & (PATH_MAX - 1)] == '/') {
            // We shift everything left by 1
            shift.start_index = index_to_push + 1;
            shift.shift = 1;
            bpf_map_update_elem(&shift_instructions, &ctx->shift_instructions_len, &shift, BPF_ANY);
            ctx->shift_instructions_len++;
            ctx->current_shift += shift.shift;
        }
        // Case of trailing slash at the end of the path '/\0' (excluding the case of the root path '/')
        else if (index > 0 && index == ctx->path_len - 2) {
            // We shift everything left by 1
            shift.start_index = index_to_push + 1;
            shift.shift = 1;
            bpf_map_update_elem(&shift_instructions, &ctx->shift_instructions_len, &shift, BPF_ANY);
            ctx->shift_instructions_len++;
            ctx->current_shift += shift.shift;
        }
        // Case of current directory '/./' (or '/.' at the end of the path)
        else if (index < ctx->path_len - 2 && ctx->path[(index + 1) & (PATH_MAX - 1)] == '.' && 
                slash_or_end(ctx->path, index + 2)) {
            // We shift everything left by 2
            shift.start_index = index_to_push + 2;
            shift.shift = 2;
            bpf_map_update_elem(&shift_instructions, &ctx->shift_instructions_len, &shift, BPF_ANY);
            ctx->shift_instructions_len++;
            ctx->current_shift += shift.shift;
        }
        // Case of parent directory '/../' (or '/..' at the end of the path)
        else if (index < ctx->path_len - 3 && ctx->path[(index + 1) & (PATH_MAX - 1)] == '.' &&
                ctx->path[(index + 2) & (PATH_MAX - 1)] == '.' && 
                slash_or_end(ctx->path, index + 3)) {
            // Retrieve the last slash index from the stack. If the stack is empty, it means we are trying to go above the root
            // and in that case we just just use the root as the base.
            int last_slash_index = pop_elem();
            if (last_slash_index == -1) {
                last_slash_index = 0;
            }

            // We need to shift everything left by the length of the last atom + 3 (for the '/../')
            int last_atom_length = index_to_push - last_slash_index;
            shift.start_index = index_to_push + 3;
            shift.shift = last_atom_length + 3;
            bpf_map_update_elem(&shift_instructions, &ctx->shift_instructions_len, &shift, BPF_ANY);
            ctx->shift_instructions_len++;
            ctx->current_shift += shift.shift;
        }
        else {
            // We found a normal slash, push its index onto the stack in case we need to pop it later
            push_elem(index_to_push);
        }
    }

    return 0;
}

/** Build shift instructions by traversing the path once and identifying patterns to remove.
 * @param path: The path to analyze.
 * @param path_len: The length of the path.
 * @param shift_instructions: A queue to hold the generated shift instructions.
 * @return: The number of shift instructions generated, or -1 if an error occurred.
 */
__attribute__((always_inline)) static int build_shift_instructions(const char * path, int path_len) {
    if (!path || path_len <= 0) {
        return 0;
    }

    struct build_shift_instructions_context ctx = {
        .path = path,
        .path_len = path_len,
        .shift_instructions_len = 0,
        .current_shift = 0,
    };

    // Make sure the stack of slash indices is initialized before starting. The callback is going to push/pop from it.
    empty_stack();

    bpf_loop(path_len, build_shift_instructions_callback, &ctx, 0);

    // Make sure we clear the stack for next time. We might not have popped everything and the next time
    // we build instructions we want to start with an empty stack.
    empty_stack();

    return ctx.shift_instructions_len;
}

/** Context for canonicalizing a path */
struct canonicalize_path_context {
    // The path to canonicalize.
    char *path;
    // The length of the path.
    int path_len;
    // The number of shift instructions generated.
    int shift_instructions_len;
    // The new length of the path after canonicalization.
    int new_path_len;
};

/**
 * canonicalize_path_callback() - Callback to apply shift instructions to canonicalize a path.
 * Traverses the shift_instructions array and applies each instruction to the path. The array is not part of the context
 * to avoid verifier issues.
 */
__attribute__((always_inline)) static int canonicalize_path_callback(u64 index, struct canonicalize_path_context *ctx) {
    struct shift_instruction* shift;
    // Stop the loop if we can't pop an instruction.
    shift = bpf_map_lookup_elem(&shift_instructions, &index);
    if (shift == NULL)
    {
        return 1;
    }

    // The index should always be in range, but let's keep the verifier happy
    if (shift->start_index < 0 || shift->start_index >= PATH_MAX || shift->start_index >= ctx->path_len) {
        return 1; 
    }

    shift_left(ctx->path, ctx->path_len, shift->start_index, shift->shift);
    // Update the new path length
    ctx->new_path_len -= shift->shift;

    // Continue to next shift instruction
    return 0;
}

/**
 * canonicalize_path() - Canonicalizes a path by removing redundant components like '.', '..', and consecutive slashes.
 * The canonicalization is done in place.
 * @param runner_pid: The PID of the runner process, used for error reporting.
 * @param path: The path to canonicalize.
 * @param path_len: The length of the path.
 * @return: the new length of the canonicalized path, or -1 if an error occurred.
 */
__attribute__((always_inline)) static int canonicalize_path(char *path, int path_len) {
    if (!path || path_len <= 0 || path_len > PATH_MAX) {
        return path_len;
    }

    // The ideal implementation would be to just shift in place as we find the patterns to remove,
    // but the verifier doesn't like that approach. So we first build a list of shift instructions
    // and then apply them one by one. In the end we still traverse each path only once - to build the shift instructions - 
    // and then we traverse the shift instructions (which are at most PATH_MAX) to apply them.
    int shift_instructions_len = build_shift_instructions(path, path_len);
    // If there are no shift instructions, just don't bother going through the motions.
    if (shift_instructions_len == 0) {
        return path_len;
    }
    else if (shift_instructions_len == -1) {
        return -1;
    }

    struct canonicalize_path_context ctx = {
        .path = path,
        .path_len = path_len,
        .new_path_len = path_len,
    };

    // The callback will go through all the shift instructions
    bpf_loop(shift_instructions_len, canonicalize_path_callback, &ctx, 0);
    
    // We have an edge case when the path is just '/..' (maybe after multiple shifts), we return the empty string.
    // In this case we want to return '/' as the canonicalized path.
    if (ctx.new_path_len == 1 && path[0] == '\0') {
        path[0] = '/';
        path[1] = '\0';
        ctx.new_path_len = 2;
    }

    return ctx.new_path_len;
}

#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFSTRINGUTILITIES_H