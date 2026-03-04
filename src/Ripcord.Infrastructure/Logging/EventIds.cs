using Microsoft.Extensions.Logging;

namespace Ripcord.Infrastructure.Logging
{
    /// <summary>
    /// Centralized event ids. Keep ranges per area:
    /// 1000-1199 App lifecycle, 1200-1399 UI, 1400-1599 Engine, 1600-1799 IO/FS, 1800+ Security.
    /// </summary>
    public static class EventIds
    {
        public static readonly EventId AppStart        = new(1000, nameof(AppStart));
        public static readonly EventId AppExit         = new(1001, nameof(AppExit));
        public static readonly EventId UnhandledError  = new(1002, nameof(UnhandledError));

        public static readonly EventId UiAction        = new(1200, nameof(UiAction));
        public static readonly EventId UiDrop          = new(1201, nameof(UiDrop));

        public static readonly EventId EngineJobStart  = new(1400, nameof(EngineJobStart));
        public static readonly EventId EngineJobDone   = new(1401, nameof(EngineJobDone));
        public static readonly EventId EngineJobFail   = new(1402, nameof(EngineJobFail));
        public static readonly EventId EnginePass      = new(1403, nameof(EnginePass));

        public static readonly EventId IoOpen          = new(1600, nameof(IoOpen));
        public static readonly EventId IoWrite         = new(1601, nameof(IoWrite));
        public static readonly EventId IoDelete        = new(1602, nameof(IoDelete));
        public static readonly EventId IoTrim          = new(1603, nameof(IoTrim));

        public static readonly EventId SecurityCheck   = new(1800, nameof(SecurityCheck));
    }
}
