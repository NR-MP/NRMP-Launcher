using System;
using System.Net;
using System.Windows.Forms;

namespace NightRunnersMP.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // nothing here uses a proxy. set before the first request or .net goes and reads the windows internet settings
        WebRequest.DefaultWebProxy = null;

        try
        {
            if (SelfInstall.Run())
                return 0;
        }
        catch (Exception e)
        {
            MessageBox.Show($"Could not install the launcher: {e.Message}", "Night-Runners MultiPlayer");
            return 1;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}
