using System;
using System.Windows;
using System.Windows.Controls;

namespace VeriPlayer.Views.Panels
{
    public partial class ConfirmPanel : UserControl, IModalPanel
    {
        public event EventHandler CloseRequested;
        public event Action Confirmed;

        public string Title => "Подтверждение";

        public ConfirmPanel(string question, string confirmText)
        {
            InitializeComponent();
            Question.Text = question;
            ConfirmButton.Content = confirmText;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
