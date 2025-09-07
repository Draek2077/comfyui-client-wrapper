using System.Windows;

namespace ComfyUIClientWrapper
{
    public partial class AboutWindow : Window
    {
        public AboutWindow(string version)
        {
            InitializeComponent();
            // Set the version text from the value passed in
            VersionTextBlock.Text = $"Version {version}";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the dialog when "OK" is clicked
            this.Close();
        }
    }
}