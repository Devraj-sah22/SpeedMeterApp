using System.Windows;

namespace SpeedMeterApp
{
    public partial class App : System.Windows.Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    
    private void Application_Activated(object sender, EventArgs e)
    {
        // Ensure our windows stay on top
        foreach (Window window in this.Windows)
        {
            window.Topmost = true;
        }
    }
    }

}