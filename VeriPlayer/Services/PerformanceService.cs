using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Облегчённый режим для слабых ПК: без анимаций, частиц, спектра и клипов на фоне.
    /// Включается сам на слабом железе; пользователь может включить или выключить его вручную.
    /// </summary>
    public static class PerformanceService
    {
        private const long FourGb = 4L * 1024 * 1024 * 1024;

        public static bool WeakHardware { get; private set; }
        /// <summary>Почему компьютер признан слабым — для подсказки в настройках.</summary>
        public static string Reason { get; private set; } = "";

        /// <summary>Вызывать в UI-потоке: оценка видеокарты WPF доступна только там.</summary>
        public static void Detect()
        {
            var reasons = new List<string>();
            int tier = RenderCapability.Tier >> 16;
            if (tier < 2) reasons.Add(tier == 0 ? "нет аппаратного ускорения графики" : "слабая видеокарта");
            if (Environment.ProcessorCount <= 2) reasons.Add(Environment.ProcessorCount + " ядра процессора");
            long memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (memory > 0 && memory < FourGb) reasons.Add("меньше 4 ГБ оперативной памяти");

            WeakHardware = reasons.Count > 0;
            Reason = string.Join(", ", reasons);
        }

        public static bool IsLite => App.Settings.LiteMode ?? WeakHardware;

        public static bool ReduceMotion => IsLite || App.Settings.ReduceMotion;
        public static bool Visualizer => !IsLite && App.Settings.Visualizer;
        public static bool Particles => !IsLite;
        public static bool Clips => !IsLite;
        public static bool BlurBackground => !IsLite;
        /// <summary>Размер, до которого декодируем большую обложку: меньше — меньше памяти и быстрее.</summary>
        public static int HeroCoverPixels => IsLite ? 480 : 900;
    }
}
