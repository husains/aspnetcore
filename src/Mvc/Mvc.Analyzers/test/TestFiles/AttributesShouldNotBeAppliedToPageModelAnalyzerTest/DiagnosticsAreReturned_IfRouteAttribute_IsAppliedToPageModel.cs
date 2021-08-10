﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Microsoft.AspNetCore.Mvc.Analyzers.Test
{
    [/*MM*/Route("/mypage")]
    public class DiagnosticsAreReturned_IfRouteAttribute_IsAppliedToPageModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
