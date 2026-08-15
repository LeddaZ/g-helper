using System.Timers;

namespace GHelper.Helpers
{
    public static class TimerHelper
    {
        /// <summary>
        /// System.Timers.Timer swallows every exception an Elapsed handler throws. That can't kill
        /// the process, but it does abort the rest of the handler with no trace at all, so the app
        /// silently stops doing part of its job. Wrapping keeps the same safety and logs the reason.
        /// </summary>
        public static ElapsedEventHandler Guarded(string name, ElapsedEventHandler handler)
        {
            return (sender, e) =>
            {
                try
                {
                    handler(sender, e);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"{name} timer: {ex}");
                }
            };
        }
    }
}
