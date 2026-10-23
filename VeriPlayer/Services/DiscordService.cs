using System;
using DiscordRPC;
using DiscordRPC.Logging;
using VeriPlayer.Models;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Показывает текущий трек в Discord через Rich Presence (как Spotify).
    /// </summary>
    public static class DiscordService
    {
        private static DiscordRpcClient _client;
        private static bool _initialized;
        
        // Discord Application ID — нужно зарегистрировать приложение на https://discord.com/developers/applications
        // Для теста используем заглушку; в реальности нужен настоящий ID
        private const string ApplicationId = "1234567890123456789";

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                _client = new DiscordRpcClient(ApplicationId);
                _client.Logger = new ConsoleLogger { Level = LogLevel.Warning };
                
                _client.OnReady += (sender, e) =>
                {
                    Diagnostics.Log("Discord RPC connected: " + e.User.Username);
                };

                _client.OnError += (sender, e) =>
                {
                    Diagnostics.Log("Discord RPC error: " + e.Message);
                };

                _client.Initialize();
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Discord RPC init failed: " + ex.Message);
            }
        }

        public static void UpdatePresence(Track track, bool playing)
        {
            if (_client == null || !_client.IsInitialized) return;

            try
            {
                if (track == null || !playing)
                {
                    _client.ClearPresence();
                    return;
                }

                var presence = new RichPresence
                {
                    Details = track.DisplayTitle,
                    State = "от " + track.DisplayArtist,
                    Assets = new Assets
                    {
                        LargeImageKey = "veri_logo", // нужно загрузить в Discord приложение
                        LargeImageText = "Veri Player",
                        SmallImageKey = playing ? "play" : "pause",
                        SmallImageText = playing ? "Играет" : "На паузе"
                    }
                };

                if (track.Duration > 0 && playing)
                {
                    var elapsed = AudioService.Position;
                    var remaining = track.Duration - elapsed;
                    if (remaining > 0)
                    {
                        presence.Timestamps = new Timestamps
                        {
                            End = DateTime.UtcNow.AddSeconds(remaining)
                        };
                    }
                }

                _client.SetPresence(presence);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Discord RPC update failed: " + ex.Message);
            }
        }

        public static void Clear()
        {
            try
            {
                _client?.ClearPresence();
            }
            catch { }
        }

        public static void Shutdown()
        {
            try
            {
                _client?.Dispose();
            }
            catch { }
            _client = null;
            _initialized = false;
        }
    }
}
