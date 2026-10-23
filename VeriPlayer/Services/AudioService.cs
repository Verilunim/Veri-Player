using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Воспроизведение + спектр для визуализатора. Библиотека музыки пустая изначально —
    /// играем только то, что пользователь добавил сам.
    /// </summary>
    public static class AudioService
    {
        public const int SpectrumBins = 48;

        private static IWavePlayer _output;
        private static WaveStream _reader;
        private static FftTap _tap;
        private static DispatcherTimer _timer;
        private static bool _suppressStop;
        private static bool _initialised;
        private static double _volume = 0.8;

        public static event Action<Track> TrackChanged;
        public static event Action<bool> StateChanged;
        public static event Action<double> PositionTick;
        public static event Action<float[]> Spectrum;
        public static event Action<string> Failed;

        public static List<Track> Queue { get; private set; } = new List<Track>();
        public static string QueueName { get; set; } = "Библиотека";
        public static int Index { get; private set; } = -1;

        public static Track Current => (Index >= 0 && Index < Queue.Count) ? Queue[Index] : null;
        public static bool HasTrack => Current != null;
        public static bool IsPlaying => _output != null && _output.PlaybackState == PlaybackState.Playing;
        public static bool Shuffle { get; set; }
        public static string Repeat { get; set; } = "off";

        public static double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0, Math.Min(1, value));
                if (_output != null) _output.Volume = (float)_volume;
            }
        }

        public static double Position
        {
            get
            {
                try { return _reader?.CurrentTime.TotalSeconds ?? 0; }
                catch { return 0; }
            }
        }

        public static double Duration
        {
            get
            {
                try
                {
                    if (_reader != null && _reader.TotalTime.TotalSeconds > 0.2) return _reader.TotalTime.TotalSeconds;
                }
                catch { }
                return Current?.Duration ?? 0;
            }
        }

        // ------------------------------------------------------------------ setup

        public static void Init()
        {
            if (_initialised) return;
            _initialised = true;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += (s, e) =>
            {
                if (IsPlaying) PositionTick?.Invoke(Position);
            };
            _timer.Start();
        }

        // ------------------------------------------------------------------ queue

        public static void SetQueue(IEnumerable<Track> tracks, int startIndex = 0, string name = null)
        {
            Queue = tracks?.ToList() ?? new List<Track>();
            if (name != null) QueueName = name;
            if (startIndex < 0 || startIndex >= Queue.Count) startIndex = Queue.Count > 0 ? 0 : -1;
            Index = startIndex;
            if (Index >= 0) PlayIndex(Index);
        }

        public static void PlayTrack(Track track, IEnumerable<Track> context = null, string contextName = null)
        {
            if (track == null) return;
            if (context != null)
            {
                Queue = context.ToList();
                QueueName = contextName ?? QueueName;
                Index = Queue.IndexOf(track);
                if (Index < 0) { Queue.Insert(0, track); Index = 0; }
            }
            else
            {
                Index = Queue.IndexOf(track);
                if (Index < 0)
                {
                    Queue.Add(track);
                    Index = Queue.Count - 1;
                }
            }
            CrossFadeTo(Index);
        }

        // ------------------------------------------------------------------ плавные переходы

        private static DispatcherTimer _fadeTimer;
        private static float _fadeFrom;
        private static float _fadeTo;
        private static double _fadeElapsed;
        private static double _fadeDuration;
        private static Action _fadeDone;

        /// <summary>Плавно меняет громкость и вызывает onDone в конце. durationMs &lt;= 0 — мгновенно.</summary>
        public static void FadeTo(double target, double durationMs, Action onDone = null)
        {
            if (_fadeTimer == null)
            {
                _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _fadeTimer.Tick += FadeTick;
            }

            _fadeTimer.Stop();
            _fadeFrom = _output != null ? _output.Volume : 0f;
            _fadeTo = (float)Math.Max(0, Math.Min(1, target));
            _fadeDone = onDone;

            if (durationMs <= 0 || _output == null)
            {
                if (_output != null) _output.Volume = _fadeTo;
                var done = _fadeDone; _fadeDone = null;
                done?.Invoke();
                return;
            }

            _fadeElapsed = 0;
            _fadeDuration = durationMs;
            _fadeTimer.Start();
        }

        private static void FadeTick(object sender, EventArgs e)
        {
            if (_output == null) { _fadeTimer.Stop(); return; }

            _fadeElapsed += 16;
            float t = (float)Math.Clamp(_fadeElapsed / _fadeDuration, 0, 1);
            // сглаживание, чтобы громкость нарастала мягко, а не линейно
            float eased = t * t * (3f - 2f * t);
            _output.Volume = _fadeFrom + (_fadeTo - _fadeFrom) * eased;

            if (t >= 1f)
            {
                _fadeTimer.Stop();
                var done = _fadeDone; _fadeDone = null;
                done?.Invoke();
            }
        }

        /// <summary>Мягко останавливает текущий трек, затем запускает новый с плавным появлением звука.</summary>
        private static void CrossFadeTo(int index, double outMs = 420, double inMs = 520)
        {
            if (index < 0 || index >= Queue.Count) return;

            if (_output == null || !IsPlaying || outMs <= 0)
            {
                // нечего глушить (трек уже закончился или ещё не играл) —
                // всё равно включаем следующий с плавным нарастанием, чтобы начало было слышно
                PlayIndex(index, inMs);
                return;
            }

            // глушим старый трек и запускаем новый — переход без резкого обрыва
            FadeTo(0, outMs, () => PlayIndex(index, inMs));
        }

        public static void PlayIndex(int index, double fadeInMs = 0)
        {
            if (index < 0 || index >= Queue.Count) return;
            Index = index;
            var track = Queue[index];

            _fadeTimer?.Stop();
            _fadeDone = null;

            Teardown();
            _suppressStop = true;

            try
            {
                _reader = CreateReader(track.FilePath);
            }
            catch
            {
                _suppressStop = false;
                Failed?.Invoke("Не удалось открыть «" + track.DisplayTitle + "». Формат не поддерживается или файл повреждён.");
                return;
            }

            _output = new WaveOutEvent { DesiredLatency = 180, NumberOfBuffers = 3 };
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Volume = (float)_volume;

            bool ok = false;
            try
            {
                var sampleProvider = AsSampleProvider(_reader);
                _tap = new FftTap(sampleProvider, SpectrumBins);
                _tap.Spectrum += bins => Spectrum?.Invoke(bins);
                _output.Init(_tap.ToWaveProvider());
                ok = true;
            }
            catch
            {
                try
                {
                    _output.Dispose();
                    _output = new WaveOutEvent { DesiredLatency = 180, NumberOfBuffers = 3 };
                    _output.PlaybackStopped += OnPlaybackStopped;
                    _output.Volume = (float)_volume;
                    _output.Init(_reader);
                    ok = true;
                }
                catch (Exception ex2)
                {
                    Failed?.Invoke("Не удалось воспроизвести «" + track.DisplayTitle + "»: " + ex2.Message);
                }
            }

            if (!ok)
            {
                _suppressStop = false;
                Teardown();
                return;
            }

            try { _output.Play(); }
            catch { }

            _suppressStop = false;

            if (fadeInMs > 0)
            {
                _output.Volume = 0f;
                FadeTo(_volume, fadeInMs);
            }

            UserDataService.PushRecent(track.Id);
            LibraryService.RegisterPlay(track);

            TrackChanged?.Invoke(track);
            StateChanged?.Invoke(IsPlaying);
            PositionTick?.Invoke(0);
        }

        private static WaveStream CreateReader(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Файл не найден", path);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp3" || ext == ".wav" || ext == ".aiff" || ext == ".aif")
                return new AudioFileReader(path);

            try { return new AudioFileReader(path); }
            catch { return new MediaFoundationReader(path); }
        }

        private static ISampleProvider AsSampleProvider(WaveStream stream)
        {
            if (stream is ISampleProvider sp) return sp;
            return stream.ToSampleProvider();
        }

        // ------------------------------------------------------------------ transport

        public static void TogglePlay()
        {
            if (_output == null)
            {
                if (Queue.Count == 0) return;
                PlayIndex(Index >= 0 ? Index : 0);
                return;
            }

            if (_output.PlaybackState == PlaybackState.Playing)
            {
                _output.Pause();
            }
            else
            {
                _output.Play();
            }
            StateChanged?.Invoke(IsPlaying);
        }

        public static void Stop()
        {
            _suppressStop = true;
            try { _output?.Stop(); } catch { }
            Teardown();
            _suppressStop = false;
            StateChanged?.Invoke(false);
            PositionTick?.Invoke(0);
        }

        public static void Next(bool byUser = true)
        {
            if (Queue.Count == 0) return;

            if (Shuffle && Queue.Count > 1)
            {
                var rnd = new Random();
                int next = Index;
                for (int guard = 0; guard < 40 && next == Index; guard++)
                    next = rnd.Next(Queue.Count);
                CrossFadeTo(next);
                return;
            }

            if (Index + 1 < Queue.Count) { CrossFadeTo(Index + 1); return; }
            if (Repeat == "all" || byUser) { CrossFadeTo(0); return; }
            Stop();
        }

        public static void Previous()
        {
            if (Queue.Count == 0) return;

            if (Position > 4)
            {
                Seek(0);
                return;
            }

            if (Shuffle && Queue.Count > 1)
            {
                var rnd = new Random();
                int prev = Index;
                for (int guard = 0; guard < 40 && prev == Index; guard++)
                    prev = rnd.Next(Queue.Count);
                CrossFadeTo(prev);
                return;
            }

            if (Index - 1 >= 0) CrossFadeTo(Index - 1);
            else CrossFadeTo(Queue.Count - 1);
        }

        public static void Seek(double seconds)
        {
            if (_reader == null) return;
            try
            {
                var total = Duration;
                if (total <= 0) return;
                _reader.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, Math.Min(total, seconds)));
                PositionTick?.Invoke(Position);
            }
            catch { }
        }

        public static void SeekPercent(double percent)
        {
            var total = Duration;
            if (total > 0) Seek(total * Math.Max(0, Math.Min(1, percent)));
        }

        // ------------------------------------------------------------------ internals

        private static void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (_suppressStop) return;

                if (e != null && e.Exception != null)
                {
                    Failed?.Invoke("Ошибка воспроизведения: " + e.Exception.Message);
                    return;
                }

                if (Repeat == "one" && HasTrack)
                {
                    PlayIndex(Index, 350);
                    return;
                }

                if (Index + 1 < Queue.Count || Repeat == "all")
                {
                    Next(false);
                }
                else
                {
                    StateChanged?.Invoke(false);
                    Teardown();
                    PositionTick?.Invoke(0);
                }
            }));
        }

        private static void Teardown()
        {
            try { if (_output != null) { _output.PlaybackStopped -= OnPlaybackStopped; _output.Stop(); _output.Dispose(); } } catch { }
            _output = null;
            try { _reader?.Dispose(); } catch { }
            _reader = null;
            _tap = null;
        }

        public static void Shutdown() => Teardown();

        // ------------------------------------------------------------------ fft

        private sealed class FftTap : ISampleProvider
        {
            private const int FftSize = 2048;
            private const int FftM = 11; // log2(2048)

            private readonly ISampleProvider _source;
            private readonly float[] _buffer = new float[FftSize];
            private readonly Complex[] _fft = new Complex[FftSize];
            private readonly float[] _window = new float[FftSize];
            private readonly int _bins;
            private int _position;

            public event Action<float[]> Spectrum;

            public FftTap(ISampleProvider source, int bins)
            {
                _source = source;
                _bins = bins;
                for (int i = 0; i < FftSize; i++)
                    _window[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1))));
            }

            public WaveFormat WaveFormat => _source.WaveFormat;

            public int Read(float[] buffer, int offset, int count)
            {
                int read = _source.Read(buffer, offset, count);
                // сэмплы идут вперемешку по каналам (L R L R…) — сводим кадр в моно,
                // иначе FFT видит удвоенную частоту и спектр «уезжает» вверх
                int channels = Math.Max(1, WaveFormat.Channels);
                for (int i = 0; i + channels <= read; i += channels)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++) sum += buffer[offset + i + c];
                    _buffer[_position++] = sum / channels;
                    if (_position >= FftSize)
                    {
                        _position = 0;
                        Process();
                    }
                }
                return read;
            }

            private void Process()
            {
                for (int i = 0; i < FftSize; i++)
                {
                    _fft[i].X = (float)(_buffer[i] * _window[i]);
                    _fft[i].Y = 0f;
                }

                FastFourierTransform.FFT(true, FftM, _fft);

                var output = new float[_bins];
                double sampleRate = WaveFormat.SampleRate;
                const double minFreq = 32.0;
                const double maxFreq = 16000.0;
                double ratio = maxFreq / minFreq;

                for (int b = 0; b < _bins; b++)
                {
                    double f0 = minFreq * Math.Pow(ratio, (double)b / _bins);
                    double f1 = minFreq * Math.Pow(ratio, (double)(b + 1) / _bins);
                    int i0 = (int)(f0 * FftSize / sampleRate);
                    int i1 = (int)(f1 * FftSize / sampleRate);
                    if (i0 < 1) i0 = 1;
                    if (i1 <= i0) i1 = i0 + 1;
                    if (i1 > FftSize / 2 - 1) i1 = FftSize / 2 - 1;

                    double peak = 0;
                    for (int i = i0; i <= i1; i++)
                    {
                        double mag = Math.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
                        if (mag > peak) peak = mag;
                    }

                    double db = 20 * Math.Log10(peak + 1e-9);
                    double value = (db + 72.0) / 72.0;
                    output[b] = (float)Math.Max(0, Math.Min(1, value));
                }

                Spectrum?.Invoke(output);
            }
        }
    }
}
