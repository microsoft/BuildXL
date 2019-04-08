// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <sstream>
#include <iomanip>

#include "arg_parse.hpp"
#include "BuildXLSandboxShared.hpp"

static const char *GetTypeName(const type_info *ti)
{
    if (*ti == typeid(int)) return "int";
    if (*ti == typeid(bool)) return "bool";
    if (*ti == typeid(string)) return "string";
    if (*ti == typeid(void)) return "";
    return "unknown";
}

bool ConfigImpl::parse(const type_info *ti, const char *value, void *result)
{
    if (*ti == typeid(int))
    {
        char *endPtr = nullptr;
        *reinterpret_cast<int*>(result) = (int)strtol(value, &endPtr, 10);
        return endPtr && *endPtr == '\0';
    }
    else if (*ti == typeid(string))
    {
        *(reinterpret_cast<string*>(result)) = value;
        return true;
    }
    else if (*ti == typeid(bool))
    {
        if (strcmp(value, "") == 0 || strcmp(value, "true") == 0)
        {
            *(reinterpret_cast<bool*>(result)) = true;
            return true;
        }
        else if (strcmp(value, "false") == 0)
        {
            *(reinterpret_cast<bool*>(result)) = false;
            return true;
        }
        else
        {
            return false;
        }
    }

    return true;
}

void ConfigImpl::printUsage() const
{
    cout << "OPTIONS" << endl;
    for (int i = 0; i < argCount_; i++)
    {
        Arg arg = args_[i];

        stringstream longSw;
        longSw << "--" << arg.Meta.LongName();
        if (!arg.IsFlag())
            longSw << " <" << GetTypeName(arg.Type) << ">";

        stringstream shortSw;
        shortSw << "-" << arg.Meta.ShortName();

        cout << "  " << setw(30) << left << longSw.str()
             << " | "  << setw(5)  << left << shortSw.str()
             << " :: " << right << arg.Meta.Description();

        if (arg.Meta.IsRequired())
        {
            cout << " Required.";
        }
        else if (!arg.IsFlag())
        {
            cout << " Default: " << arg.Default << ".";
        }

        cout << endl;
    }
}

bool ConfigImpl::parse(int argc, const char *argv[]) const
{
    int argvIdx = 1;
    while (argvIdx < argc)
    {
        const char *arg = argv[argvIdx++];
        const Arg *match = nullptr;
        const char *boolArgValue = "true";
        for (int optsIdx = 0; optsIdx < argCount_; optsIdx++)
        {
            ArgMeta meta = args_[optsIdx].Meta;
            if (arg == string("--") + meta.LongName() ||
                arg == string("-") + meta.ShortName())
            {
                match = &args_[optsIdx];
                break;
            }

            if (args_[optsIdx].IsFlag() &&
                (arg == string("--no-") + meta.LongName() ||
                 arg == string("-no-") + meta.ShortName()))
            {
                match = &args_[optsIdx];
                boolArgValue = "false";
                break;
            }
        }

        if (!match)
        {
            error("Unknown option: '%s'", arg);
            return false;
        }

        const char *argValue;
        if (match->IsFlag())
        {
            argValue = boolArgValue;
        }
        else
        {
            if (argvIdx >= argc)
            {
                error("No value for option '%s'", arg);
                return false;
            }

            argValue = argv[argvIdx++];
        }

        if (!match->Parser(config_, argValue))
        {
            error("Could not parse value '%s' for arg '%s' whose type is expected to be '%s'",
                  argv[argvIdx-1], arg, GetTypeName(match->Type));
            return false;
        }
    }

    return true;
}
