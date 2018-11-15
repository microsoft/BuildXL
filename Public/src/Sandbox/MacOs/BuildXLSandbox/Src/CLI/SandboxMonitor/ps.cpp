//
//  ps.cpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "ps.hpp"
#include <array>
#include <sstream>
#include <regex>

string exec(const char *cmd)
{
    array<char, 128> buffer;
    string result;
    shared_ptr<FILE> pipe(popen(cmd, "r"), pclose);
    if (!pipe) return "";
    while (!feof(pipe.get()))
    {
        if (fgets(buffer.data(), 128, pipe.get()) != nullptr)
            result += buffer.data();
    }
    return result;
}

string ps(pid_t pid, const string &cols)
{
    stringstream str;
    str << "ps -p " << pid << " -o " << cols;
    string stdout = exec(str.str().c_str());
    return regex_replace(stdout, regex("\\s*\n$"), "");
}
