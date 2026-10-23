using System;

namespace VeriPlayer.Views.Panels
{
    /// <summary>Любая панель, которую можно показать поверх приложения.</summary>
    public interface IModalPanel
    {
        event EventHandler CloseRequested;
        string Title { get; }
    }
}
