using GasAgencyManager.Data;
using GasAgencyManager.Models;
using System.Windows;
using System.Windows.Input;

namespace GasAgencyManager
{
    public partial class LoginWindow : Window
    {
        private readonly DatabaseService _db;
        public User? AuthenticatedUser { get; private set; }

        public LoginWindow(DatabaseService db)
        {
            InitializeComponent();
            _db = db;
            Loaded += (_, __) =>
            {
                UsernameBox.Focus();
                UsernameBox.KeyDown += HandleEnter;
                PasswordBox.KeyDown += HandleEnter;
            };
        }

        private void HandleEnter(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Login_Click(this, new RoutedEventArgs());
            }
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = "";
            var username = UsernameBox.Text?.Trim() ?? "";
            var password = PasswordBox.Password ?? "";

            try
            {
                var user = _db.Authenticate(username, password);
                if (user == null)
                {
                    ErrorText.Text = "Invalid username or password.";
                    PasswordBox.Clear();
                    PasswordBox.Focus();
                    return;
                }

                AuthenticatedUser = user;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                // Avoid crashing the app on DB/schema issues
                ErrorText.Text = "Login error: " + ex.Message;
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}



