using System.Text.RegularExpressions;

namespace Oconde.Radar
{
    internal static class Protocol
    {
        public const int Version = 1;
        public const string IngestPath = "/api/v1/events";
        public const string CheckInPath = "/api/v1/checkins/";
        public const int BatchEvents = 500;
        public const int MessageLength = 2000;
        public const int StringLength = 2000;
        public const int AttributesBytes = 16384;
        public const int BodyBytes = 16384;
        public const int MaxHeaders = 50;
        public const int MaxFrames = 50;
        public const int ContextFrames = 10;
        public const int ContextLines = 5;
        public const int ContextLineLength = 300;
        public const int CauseDepth = 5;
        public const int MaxDepth = 6;
        public const int ArrayItems = 50;
        public const int RequestIdLength = 128;
        public const int CheckInIdLength = 64;
        public const long MaxSafeInteger = 9007199254740991L;
        public static readonly Regex CheckInSlug = new Regex("^[a-z0-9][a-z0-9-]{0,63}$");
    }
}
