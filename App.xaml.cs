using GasAgencyManager.Data;
using GasAgencyManager.Models;
using System.Windows;
using System.Windows.Threading;

namespace GasAgencyManager
{
    public partial class App : Application
    {
        public static User? CurrentUser { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{args.Exception.Message}",
                    "Application Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            // Prevent app from closing when the login window closes
            // (there is a brief moment with zero windows before MainWindow is shown).
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Initialize local database
            var db = new DatabaseService();
            db.InitializeDatabase();
            db.EnsureMorningUpliftsTable();
            db.EnsureStockRowsExist();
            db.SeedLogisticsData();
            db.EnsureDefaultUsers();

            // Login first
            var login = new LoginWindow(db);
            var result = login.ShowDialog();
            if (result != true || login.AuthenticatedUser == null)
            {
                Shutdown();
                return;
            }

            CurrentUser = login.AuthenticatedUser;

            var main = new MainWindow();
            MainWindow = main;
            main.Show();

            // Now we have the main window, restore normal shutdown behavior.
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
    }
}

