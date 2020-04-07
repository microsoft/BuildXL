// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

#include <limits.h>
#include <stddef.h>

#include "Sandbox.hpp"
#include "SandboxedPip.hpp"

#define BxlEnvFamPath "__BUILDXL_FAM_PATH"
#define BxlEnvLogPath "__BUILDXL_LOG_PATH"

#define ARRAYSIZE(arr) (sizeof(arr)/sizeof(arr[0]))

#define GEN_REAL(ret, name, ...) \
    typedef ret (*fn_real_##name)(__VA_ARGS__); \
    static fn_real_##name real_##name = NULL; \
    if (!real_##name) { real_##name = (fn_real_##name)dlsym(RTLD_NEXT, #name); assert(real_##name); }

#define _fatal(fmt, ...) do { fprintf(stderr, "(%s) " fmt "\n", __func__, __VA_ARGS__); _exit(1); } while (0)
#define fatal(msg) _fatal("%s", msg)

class BxlObserver final
{
private:
    BxlObserver();
    ~BxlObserver() {}
    BxlObserver(const BxlObserver&) = delete;
    BxlObserver& operator = (const BxlObserver&) = delete;

    std::shared_ptr<SandboxedPip> pip_;
    std::shared_ptr<SandboxedProcess> process_;
    Sandbox *sandbox_;

    static BxlObserver *sInstance;

    bool Send(const char *buf, size_t bufsiz);

public:
    bool SendReport(AccessReport &report);

    inline std::shared_ptr<SandboxedPip> GetPip()         { return pip_; }
    inline std::shared_ptr<SandboxedProcess> GetProcess() { return process_; }
    inline Sandbox* GetSandbox()                          { return sandbox_; }
    inline const char* GetReportsPath()                   { int len; return pip_->GetReportsPath(&len); }

    static BxlObserver* GetInstance(); 
};
