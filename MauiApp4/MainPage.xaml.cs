namespace project;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        MessageLabel.Text = "";
        var email = UsernameEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("Error", "Please enter both email and password.", "OK");
            return;
        }

        try
        {
            // Login — no email verification check, just authenticate
            var (profile, isVerified) = await FirebaseService.LoginAsync(email, password);

            if (profile == null)
            {
                MessageLabel.Text = "Invalid email or password.";
                return;
            }

            // Go straight to the app
            await Navigation.PushAsync(new HomePage());
        }
        catch (Exception ex)
        {
            MessageLabel.Text = "Login failed. Please try again.";
        }
    }

    private async void OnForgotPasswordTapped(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new ForgotPasswordPage());
    }

    private async void OnTopSignIn(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new Signup());
    }

    private void OnShowPasswordTapped(object sender, EventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
    }
}