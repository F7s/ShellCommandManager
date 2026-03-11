using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ShellCommandManager
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            try
            {
                NormalizeProcessPath();
                Bootstrap.Initialize(0x00010008);
            }
            catch
            {
                // Fallback: app may still run when runtime is already initialized.
            }

            InitializeComponent();
        }

        private static void NormalizeProcessPath()
        {
            try
            {
                string machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? string.Empty;
                string userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
                string currentPath = Environment.GetEnvironmentVariable("Path") ?? string.Empty;
                string windowsApps = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft",
                    "WindowsApps");

                HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
                StringBuilder merged = new();
                foreach (string segment in $"{machinePath};{userPath};{currentPath};{windowsApps}".Split(';'))
                {
                    string part = segment.Trim();
                    if (string.IsNullOrWhiteSpace(part) || !seen.Add(part))
                    {
                        continue;
                    }

                    if (merged.Length > 0)
                    {
                        merged.Append(';');
                    }

                    merged.Append(part);
                }

                Environment.SetEnvironmentVariable("Path", merged.ToString(), EnvironmentVariableTarget.Process);
            }
            catch
            {
                // Ignore env patch failures; keep startup resilient.
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }

    }
}
