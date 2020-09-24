// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <stdarg.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "utils.h"

#define ARRAYSIZE(arr) (sizeof(arr)/sizeof(arr[0]))
#define PATH_SEP_CHAR ':'

/**
 * Tries to match 'prefix' from the beggining of 'src'.
 * Upon success, returns a pointer to the next character (right after 
 * the matched prefix) in 'src' is returned; otherwise returns NULL.
 */
const char* skip_prefix(const char *src, const char *prefix)
{
    if (src == NULL || prefix == NULL)
    {
        return NULL;
    }

    while (*src && *prefix && *src++ == *prefix++);
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

/**
 * envPrefix is in the format of the env name and "=", e.g. LD_PRELOAD=
 * Append the 'value_to_add' to 'src' if 
 * 1) 'src' begins with envPrefix;
 * 2)  the 'value_to_add' does not exist in colon-separated values of envPrefix<values>;
 * Otherwise returns the original value provided in 'src'.
 * 
 * The part after envPrefix in 'src' is treated as a colon-separated list of values;
 * If 'value_to_add' doesn't exist in the list, it will be appended at the end of 'src' following a colon if needed.
 * 
 * 'buf' is an auxiliary buffer that must be at least as big as 'src' + '：' + 'value_to_add'.
 * 
 * The result is either stored in 'buf' or 'src' is returned; when the latter is 
 * the case, the value written in 'buf' is unspecified.  In either case, the return
 * value is a pointer to where the result is (either 'src' or 'buf').
 */
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

        if (is_null_or_empty(next))
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
        
        int prefixLen = ARRAYSIZE(envPrefix) - 1;
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