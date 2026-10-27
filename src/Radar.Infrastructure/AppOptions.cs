namespace Radar.Infrastructure
{
    public sealed class AppOptions
    {
        public const string SectionName = "App";

        /// <summary>Public URL of the web app; links in emails point here.</summary>
        public string WebBaseUrl { get; set; } = "http://localhost:3000";
    }
}