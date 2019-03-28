// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

@@public
export const extracted = Transformer.reSealPartialDirectory(importFrom("Dotnet-Runtime").Contents.all, r`win-x64`);