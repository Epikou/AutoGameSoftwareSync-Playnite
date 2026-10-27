using System.Windows.Controls;
using System.Windows.Documents;
using System.Diagnostics;

namespace AutoGameSoftwareSync.PlayniteExtension.Settings
{
    public partial class AgssSettingsView : UserControl
    {
        public AgssSettingsView()
        {
            InitializeComponent();
            AddHandler(Hyperlink.RequestNavigateEvent, new System.Windows.Navigation.RequestNavigateEventHandler((s, e) =>
            {
                Process.Start(e.Uri.AbsoluteUri);
                e.Handled = true;
            }));
        }
    }
}
