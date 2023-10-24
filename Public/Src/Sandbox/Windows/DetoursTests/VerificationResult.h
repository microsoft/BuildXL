// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

struct VerificationResult {
    bool Succeeded;

    VerificationResult() noexcept : Succeeded(true) { }
    VerificationResult(bool value) noexcept : Succeeded(value) { }

    void Combine(VerificationResult const& other) noexcept {
        Succeeded &= other.Succeeded;
    }
};
