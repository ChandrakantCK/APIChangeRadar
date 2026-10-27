using System;
using System.Collections.Generic;
using System.Text;

namespace Radar.Application.Accounts
{
    public interface IAccountService
    {
        /// <summary>
        /// Creates a user, their first organization and an Owner membership, all-or-nothing.
        /// </summary>
        Task<SignUpResult> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default);
    }
}
