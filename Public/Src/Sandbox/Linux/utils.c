// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <stdarg.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "utils.h"

#define PATH_SEP_CHAR ':'

// CODESYNC: SandboxedLinuxUtilsTest.cs
#define EVN_SEP_CHAR_FOR_TEST ';'

const char* skip_prefix(const char *src, const char *prefix)
{
    if (src == NULL || prefix == NULL)
    {
        return NULL;
    }

    while (*src && *prefix && *src == *prefix)
    {
        src++;
        prefix++;
    }
    return *prefix 
        ? NULL // prefix is not at '\0' --> no match
        : src; // prefix is at '\0' --> match, return the current position in src
}

/**
 * Create a copy of envp, which is newenvp.
 * Replace the ith element of the copy with "newKVP".
 * Return the pointer of newenvp.
 */
char** replace_KVP_in_envp(char *const envp[], int env_num, int i, const char *newKVP)
{
    char **newenvp = (char **)malloc((env_num + 1) * sizeof(char*));
    if (newenvp == NULL)
    {
        return (char**)envp;
    }

    memcpy(newenvp, envp, env_num * sizeof(char*));
    newenvp[i] = (char*)newKVP;
    newenvp[env_num] = NULL; // Last element of envp[] should be a null pointer.

    return newenvp;
}

const char* add_value_to_env(const char *src, const char *value_to_add, const char *envPrefix)
{
    const char *pSrc = skip_prefix(src, envPrefix);
    if (!pSrc || strlen(value_to_add) == 0)
    {
        return src;
    }

    while (*pSrc)
    {
        const char *next = skip_prefix(pSrc, value_to_add);
        if (next && (*next == '\0' || *next == PATH_SEP_CHAR))
        {
            // found a match --> return the original src
            return src;
        }
        else
        {
            // keep searching
            if (next == NULL) next = pSrc;
            while (*next != '\0' && *next != PATH_SEP_CHAR) next++;

            if (*next == '\0')
            {
                break;
            }
            else
            {
                pSrc = next + 1;
            }
        }
    }   

    // no match
    int srcLen = strlen(src);
    int totalLen = srcLen + strlen(value_to_add) + 1;
    char *newEnvBuffer = (char *)malloc(totalLen);
    if (newEnvBuffer == NULL)
    {
        return src;
    }

 
    char *pNewEnvBuffer = newEnvBuffer;
    strcpy(pNewEnvBuffer, src);
    pNewEnvBuffer += srcLen;

    char *pLastChar = pNewEnvBuffer-1;
    if (*pLastChar != PATH_SEP_CHAR && *pLastChar != '=')
    {
        *pNewEnvBuffer = PATH_SEP_CHAR;
        pNewEnvBuffer++;
    }

    strcpy(pNewEnvBuffer, value_to_add);
    return newEnvBuffer;
}

char* createEnv(char const *envName, const char *envValue)
{
    int envNameLen = strlen(envName); // strlen return the actual len of envName, not includeing '\0'
    int envValueLen = strlen(envValue);
    int totalLen = envNameLen + envValueLen + 1 + 1; //1 is for '=', the other 1 is for '\0'
    char *pKV = (char *)malloc(totalLen);
    if (pKV == NULL)
    {
        return NULL;
    }
    
    char *p = pKV;
    strcpy(p, envName);
    p = p + envNameLen;
    *p = '=';
    p++;
    strcpy(p, envValue);
    p = p + envValueLen;
    *p = '\0';
    return pKV;
}

char** ensure_env_value(char *const envp[], char const *envName, const char *envValue)
{
    // Finding env var in envp
    char *const *pEnv = envp;
    char *const *pEnvFound = NULL;
    int env_index = 0;
    int env_num = 0;
    
    while (pEnv && *pEnv)
    {
        if (skip_prefix(*pEnv, envName))
        {
            env_index = env_num;
            pEnvFound = pEnv;
        }

        ++pEnv;
        ++env_num;
    }

    // env var was found.
    if (pEnvFound)
    {
        const char *next = skip_prefix(*pEnvFound, envName);
        next = skip_prefix(next, "=");
        next = skip_prefix(next, envValue);

        if (next == NULL)
        {
            char *kvp = createEnv(envName, envValue);
            if(kvp != NULL)
            {
                return replace_KVP_in_envp(envp, env_num, env_index, kvp);
            }
        }
    }
    else
    {
        char *kvp = createEnv(envName, envValue);
        if(kvp != NULL)
        {
            char **newenvp = (char **)malloc((env_num + 2) * sizeof(char*));
            if (newenvp == NULL)
            {
                return (char**)envp;
            }

            memcpy(newenvp, envp, env_num * sizeof(char*));
            newenvp[env_num] = kvp;
            newenvp[env_num + 1] = NULL; // Last element of envp[] should be a null pointer.
            return newenvp;
        }
    }

    return (char**)envp;
}

char** ensure_paths_included_in_env(char *const envp[], char const *envPrefix, const char *arg, ...)
{
    // Finding env var in envp
    char *const *pEnv = envp;
    char *const *pEnvFound = NULL;
    int env_index = 0;
    int env_num = 0;
    
    while (pEnv && *pEnv)
    {
        if (skip_prefix(*pEnv, envPrefix))
        {
            env_index = env_num;
            pEnvFound = pEnv;
        }

        ++pEnv;
        ++env_num;
    }

    va_list varargs;
    va_start(varargs, arg);
    const char *pPath = arg;
    // env var was found.
    if (pEnvFound)
    {
        /** Check if the current path is in env
         * add_value_to_env returns pEnvFound if path is in it.
         * Otherwise it returns a new pointer to the env kvp string has the original value plus path.
         */
        const char *result = *pEnvFound;
        while(!is_null_or_empty(pPath))
        {
            const char *newResult = add_value_to_env(result, pPath, envPrefix);
            if(newResult != result)
            {
                result = newResult;
            }
            pPath = va_arg(varargs, char*);
        }
        
        // Case 1
        if (result == *pEnvFound)
        {     
            va_end(varargs);
            return (char**)envp; 
        } 
        // Case 2
        else 
        {     
            va_end(varargs);
            return replace_KVP_in_envp(envp, env_num, env_index, result);
        }
    }
    // Case 3
    else
    {
        // Get the total length of paths
        int totalPathsLen = 0;
        va_list copy;
        va_copy(copy, varargs);
        while(!is_null_or_empty(pPath))
        {
            totalPathsLen += strlen(pPath) + 1;
            pPath = va_arg(varargs, char*);
        }
        
        int prefixLen = strlen(envPrefix);
        int len = prefixLen + totalPathsLen;

        char *newEnv = (char *)malloc(len);
        if (newEnv == NULL)
        {
            va_end(varargs);
            return (char**)envp;
        }

        char *pNewEnv = newEnv;
        strcpy(pNewEnv, envPrefix);
        pNewEnv += prefixLen;
        pPath = arg;
        while(!is_null_or_empty(pPath))
        {
            strcpy(pNewEnv, pPath);
            pNewEnv += strlen(pPath);
            pPath = va_arg(copy, char*);
            if(!is_null_or_empty(pPath))
            {
                *pNewEnv = PATH_SEP_CHAR;
                ++pNewEnv;
            }
        }
   
        char **newenvp = (char **)malloc((env_num + 2) * sizeof(char*));
        if (newenvp == NULL)
        {
            va_end(varargs);
            return (char**)envp;
        }

        memcpy(newenvp, envp, env_num * sizeof(char*));
        newenvp[env_num] = newEnv;
        newenvp[env_num + 1] = NULL; // Last element of envp[] should be a null pointer.

        va_end(varargs);
        return newenvp;
    }
}

bool is_null_or_empty(const char *input)
{
    return !input || *input == '\0';
}

void copy_result_to_buf_for_test(char **result, char *buf)
{
    while (result && *result)
    {
        strcpy(buf, *result);
        int n = strlen(*result);
        buf[n] = EVN_SEP_CHAR_FOR_TEST;
        buf += n + 1;
        ++result;
    }

    // replace the last ';' with '\0'
    *(buf - 1) = '\0'; 
}

const bool add_value_to_env_for_test(const char *src, const char *value_to_add, const char *envPrefix, char *buf)
{
    const char *result = add_value_to_env(src, value_to_add, envPrefix);
    strcpy(buf, result);
    return result == src;
}

const bool ensure_env_value_for_test(char *const envp[], char const *envName, const char *envValue, char *buf)
{
    char **result = ensure_env_value(envp, envName, envValue);
    copy_result_to_buf_for_test(result, buf);
    return result == envp;
}

const bool ensure_2_paths_included_in_env_for_test(char *const envp[], char const *envPrefix, const char *path0, const char *path1, char *buf)
{
    char **result = ensure_paths_included_in_env(envp, envPrefix, path0, path1, NULL);
    copy_result_to_buf_for_test(result, buf);
    return result == envp;
}

const bool ensure_1_path_included_in_env_for_test(char *const envp[], char const *envPrefix, const char *path, char *buf)
{
    char **result = ensure_paths_included_in_env(envp, envPrefix, path, NULL);
    copy_result_to_buf_for_test(result, buf);
    return result == envp;
}