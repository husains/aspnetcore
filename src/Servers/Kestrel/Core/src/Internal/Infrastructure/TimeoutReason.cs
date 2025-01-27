﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    internal enum TimeoutReason
    {
        None,
        KeepAlive,
        RequestHeaders,
        ReadDataRate,
        WriteDataRate,
        RequestBodyDrain,
        TimeoutFeature,
    }
}
