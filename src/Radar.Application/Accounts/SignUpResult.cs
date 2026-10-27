using System;
using System.Collections.Generic;
using System.Text;

namespace Radar.Application.Accounts
{
    public sealed record SignUpError(string Code, string Description);

    public sealed class SignUpResult
    {
        private SignUpResult(Guid userId, Guid organizationId, IReadOnlyList<SignUpError> errors)
        {
            UserId = userId;
            OrganizationId = organizationId;
            Errors = errors;
        }

        public Guid UserId { get; }

        public Guid OrganizationId { get; }

        public IReadOnlyList<SignUpError> Errors { get; }

        public bool Succeeded => Errors.Count == 0;

        public static SignUpResult Success(Guid userId, Guid organizationId) =>
            new(userId, organizationId, []);

        public static SignUpResult Failure(params SignUpError[] errors) =>
            new(Guid.Empty, Guid.Empty, errors);
    }
}
