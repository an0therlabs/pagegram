using System;
using Windows.System;

namespace Pagegram.Services
{
    /// <summary>
    /// Следит за лимитом памяти, который Windows 10 Mobile выдаёт приложению
    /// (на 512 МБ устройствах это ~150-200 МБ). При приближении к лимиту
    /// просит страницы выкинуть всё лишнее.
    /// </summary>
    public static class LowMemory
    {
        /// <summary>Вызывается на UI-потоке, когда надо срочно ужаться.</summary>
        public static event Action Trim;

        private static Windows.UI.Core.CoreDispatcher _dispatcher;

        public static void Init(Windows.UI.Core.CoreDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            try
            {
                MemoryManager.AppMemoryUsageIncreased += (s, e) => Check();
                MemoryManager.AppMemoryUsageLimitChanging += (s, e) =>
                {
                    if (MemoryManager.AppMemoryUsage >= e.NewLimit * 8 / 10) Raise();
                };
            }
            catch
            {
                // MemoryManager может быть недоступен (десктоп без лимитов) — не страшно
            }
        }

        public static bool IsLowMemoryDevice
        {
            get
            {
                try { return MemoryManager.AppMemoryUsageLimit < 300UL * 1024 * 1024; }
                catch { return false; }
            }
        }

        private static void Check()
        {
            var level = MemoryManager.AppMemoryUsageLevel;
            if (level == AppMemoryUsageLevel.High || level == AppMemoryUsageLevel.OverLimit) Raise();
        }

        public static void Raise()
        {
            var d = _dispatcher;
            if (d == null) return;
            var _ = d.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High, () =>
            {
                Trim?.Invoke();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }
    }
}
