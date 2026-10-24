using System;

namespace VeriPlayer.Services
{
    /// <summary>
    /// Подключение к внешнему сервису: ключи приложения пользователя и токены. Хранится под DPAPI.
    /// </summary>
    public class ServiceAccount
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public int Port { get; set; }

        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public DateTime ExpiresAt { get; set; }

        public string UserName { get; set; } = "";
        public string UserId { get; set; } = "";
        public string UserUrl { get; set; } = "";

        public bool IsConnected => !string.IsNullOrWhiteSpace(RefreshToken) || (!string.IsNullOrWhiteSpace(AccessToken) && DateTime.UtcNow < ExpiresAt);
    }
}
