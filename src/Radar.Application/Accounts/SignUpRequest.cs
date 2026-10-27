using System;
using System.Collections.Generic;
using System.Text;

namespace Radar.Application.Accounts
{
    public sealed record SignUpRequest(string Email, string Password, string? OrganizationName);
}
