using System;
#if !NET35
using System.Threading.Tasks;
#endif

namespace Oconde.Radar
{
    public static class Radar
    {
        public static RadarClient Client { get; } = new RadarClient();

        public static bool IsEnabled => Client.IsEnabled;

        public static void Init(RadarOptions? options = null) => Client.Init(options);

        public static void Debug(string message, object? attributes = null) => Client.Debug(message, attributes);

        public static void Info(string message, object? attributes = null) => Client.Info(message, attributes);

        public static void Warn(string message, object? attributes = null) => Client.Warn(message, attributes);

        public static void Error(string message, object? attributes = null) => Client.Error(message, attributes);

        public static void Log(RadarLevel level, string message, object? attributes = null) => Client.Log(level, message, attributes);

        public static void CaptureException(Exception exception, object? attributes = null) => Client.CaptureException(exception, attributes);

        public static void SetUser(string? id = null, string? email = null, string? name = null) => Client.SetUser(id, email, name);

        public static RadarScope StartScope(string? requestId = null) => Client.StartScope(requestId);

        public static T Track<T>(string name, Func<T> work, object? attributes = null) => Client.Track(name, work, attributes);

        public static void Track(string name, Action work, object? attributes = null) => Client.Track(name, work, attributes);

        public static void CheckIn(string slug, CheckInStatus status, string? checkInId = null, double? durationMs = null) => Client.CheckIn(slug, status, checkInId, durationMs);

        public static T RunJob<T>(string slug, Func<T> work) => Client.RunJob(slug, work);

        public static void RunJob(string slug, Action work) => Client.RunJob(slug, work);

#if !NET35
        public static Task<T> TrackAsync<T>(string name, Func<Task<T>> work, object? attributes = null) => Client.TrackAsync(name, work, attributes);

        public static Task TrackAsync(string name, Func<Task> work, object? attributes = null) => Client.TrackAsync(name, work, attributes);

        public static Task<T> RunJobAsync<T>(string slug, Func<Task<T>> work) => Client.RunJobAsync(slug, work);

        public static Task RunJobAsync(string slug, Func<Task> work) => Client.RunJobAsync(slug, work);
#endif

        public static bool Flush(TimeSpan timeout) => Client.Flush(timeout);

        public static void Close(TimeSpan timeout) => Client.Close(timeout);
    }
}
