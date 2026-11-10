namespace Radar.Domain.Changes
{
    public enum ChangeSeverity
    {
        /// <summary>Existing clients will fail: something they send or read is gone or different.</summary>
        Breaking,

        /// <summary>May break some clients: deprecations, removed optional inputs, new response enum values.</summary>
        Warning,

        /// <summary>Safe additions.</summary>
        Info
    }
}
