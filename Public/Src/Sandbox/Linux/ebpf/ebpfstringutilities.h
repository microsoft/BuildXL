// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFSTRINGUTILITIES_H
#define __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFSTRINGUTILITIES_H

#include "vmlinux.h"
#include "kernelconstants.h"

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
    ctx->str[index & (PATH_MAX - 1)] = '\0';
    return 0; // Continue looping.
}

/**
 * nullify_string() - Nullifies a string by setting all characters to null.
 * @str: The string to nullify.
 * @str_len: The length of the string.
 */
__attribute__((always_inline)) static void nullify_string(char *str, int str_len) {
    if (!str || str_len <= 0) {
        return;
    }

    struct nullify_string_context ctx = {
        .str = str
    };

    bpf_loop(str_len, nullify_string_callback, &ctx, 0);
}

#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFSTRINGUTILITIES_H