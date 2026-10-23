using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VeriPlayer.Views.Panels
{
    public partial class PromptPanel : UserControl, IModalPanel
    {
        public event EventHandler CloseRequested;
        public event Action<string> Submitted;

        public string Title { get; }

        public PromptPanel(string title, string label, string initial, string confirmText)
        {
            InitializeComponent();
            Title = title;
            Label.Text = label;
            Input.Text = initial ?? "";
            ConfirmButton.Content = confirmText;

            Loaded += (s, e) =>
            {
                Input.Focus();
                Input.SelectAll();
            };
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Submit();
            else if (e.Key == Key.Escape) Close();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) => Submit();

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Submit()
        {
            if (string.IsNullOrWhiteSpace(Input.Text)) return;
            Submitted?.Invoke(Input.Text.Trim());
            Close();
        }

        private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
