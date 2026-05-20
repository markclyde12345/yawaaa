using Firebase.Auth;
using Firebase.Auth.Providers;
using Firebase.Database;
using Firebase.Database.Query;
using MentalWellness.Shared.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace project
{
    // ─── MODELS ───────────────────────────────────────────────

    public class UserProfile
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string UserId { get; set; }
        public string CreatedAt { get; set; }
        public bool IsEmailVerified { get; set; }
    }


    // ─── FIREBASE SERVICE ─────────────────────────────────────

    public static class FirebaseService
    {
        private const string FirebaseApiKey = "AIzaSyCoA1ekJ-b_dEIb3mFSofFDC0SAeOli3wE";
        private const string FirebaseDatabaseUrl = "https://mindbloom-d0622-default-rtdb.firebaseio.com/";
        private const string FirebaseAuthDomain = "mindbloom-d0622.firebaseapp.com";

        public static string CurrentUserId { get; private set; }
        public static string CurrentUserEmail { get; private set; }
        public static string CurrentToken { get; private set; }

        private static FirebaseClient _db;
        private static FirebaseAuthClient _auth;
        private static UserCredential _currentCredential;

        // ─── INIT ──────────────────────────────────────────────
        public static void Initialize()
        {
            var authConfig = new FirebaseAuthConfig
            {
                ApiKey = FirebaseApiKey,
                AuthDomain = FirebaseAuthDomain,
                Providers = new FirebaseAuthProvider[]
                {
                    new EmailProvider()
                }
            };

            _auth = new FirebaseAuthClient(authConfig);
            _db = new FirebaseClient(FirebaseDatabaseUrl);
        }

        // ─── REGISTER ─────────────────────────────────────────
        public static async Task<bool> RegisterAsync(
            string firstName, string lastName, string email, string password)
        {
            UserCredential result = null;

            try
            {
                result = await _auth.CreateUserWithEmailAndPasswordAsync(email, password, firstName);

                _currentCredential = result;
                CurrentUserId = result.User.Uid;
                CurrentUserEmail = email;
                CurrentToken = await result.User.GetIdTokenAsync();

                await SendEmailVerificationAsync(CurrentToken);

                var authenticatedDb = new FirebaseClient(
                    FirebaseDatabaseUrl,
                    new FirebaseOptions
                    {
                        AuthTokenAsyncFactory = () => Task.FromResult(CurrentToken)
                    });

                await authenticatedDb
                    .Child("users")
                    .Child(CurrentUserId)
                    .PutAsync(new UserProfile
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        UserId = CurrentUserId,
                        CreatedAt = DateTime.UtcNow.ToString("o"),
                        IsEmailVerified = false
                    });

                _db = authenticatedDb;

                return true;
            }
            catch (Exception)
            {
                if (result != null)
                {
                    try { await result.User.DeleteAsync(); }
                    catch { }
                }

                CurrentUserId = null;
                CurrentUserEmail = null;
                CurrentToken = null;
                _currentCredential = null;

                throw;
            }
        }

        // ─── LOGIN ────────────────────────────────────────────
        public static async Task<(UserProfile profile, bool isVerified)> LoginAsync(
            string email, string password)
        {
            var result = await _auth.SignInWithEmailAndPasswordAsync(email, password);

            _currentCredential = result;
            CurrentUserId = result.User.Uid;
            CurrentUserEmail = email;
            CurrentToken = await result.User.GetIdTokenAsync();

            _db = new FirebaseClient(
                FirebaseDatabaseUrl,
                new FirebaseOptions
                {
                    AuthTokenAsyncFactory = () => Task.FromResult(CurrentToken)
                });

            var isVerified = result.User.Info.IsEmailVerified;

            if (isVerified)
            {
                await _db.Child("users")
                    .Child(CurrentUserId)
                    .Child("IsEmailVerified")
                    .PutAsync(true);
            }

            var profile = await _db
                .Child("users")
                .Child(CurrentUserId)
                .OnceSingleAsync<UserProfile>();

            return (profile, isVerified);
        }

        // ─── EMAIL VERIFY ─────────────────────────────────────
        private static async Task SendEmailVerificationAsync(string idToken)
        {
            using var client = new HttpClient();

            var payload = new { requestType = "VERIFY_EMAIL", idToken };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await client.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseApiKey}",
                content);
        }

        // ─── LOGOUT ───────────────────────────────────────────
        public static void Logout()
        {
            CurrentUserId = null;
            CurrentUserEmail = null;
            CurrentToken = null;
            _currentCredential = null;

            _db = new FirebaseClient(FirebaseDatabaseUrl);
        }

        // ─── SAFETY CHECK ─────────────────────────────────────
        private static void EnsureUser()
        {
            if (string.IsNullOrEmpty(CurrentUserId))
                throw new Exception("User not logged in");
        }

        // ─── ATTEMPTS ─────────────────────────────────────────
        public static async Task<int> IncrementAttemptsAsync()
        {
            EnsureUser();

            var refDb = _db.Child("users").Child(CurrentUserId).Child("totalAttempts");
            var current = await refDb.OnceSingleAsync<int?>();
            int newValue = (current ?? 0) + 1;

            await refDb.PutAsync(newValue);
            return newValue;
        }

        public static async Task<int> GetTotalAttemptsAsync()
        {
            EnsureUser();

            var result = await _db.Child("users")
                .Child(CurrentUserId)
                .Child("totalAttempts")
                .OnceSingleAsync<int?>();

            return result ?? 0;
        }

        // ─── LOGS ─────────────────────────────────────────────
        public static async Task<int> IncrementLogsAsync()
        {
            EnsureUser();

            var refDb = _db.Child("users").Child(CurrentUserId).Child("totalLogs");
            var current = await refDb.OnceSingleAsync<int?>();
            int newValue = (current ?? 0) + 1;

            await refDb.PutAsync(newValue);
            return newValue;
        }

        public static async Task<int> GetTotalLogsAsync()
        {
            EnsureUser();

            var result = await _db.Child("users")
                .Child(CurrentUserId)
                .Child("totalLogs")
                .OnceSingleAsync<int?>();

            return result ?? 0;
        }

        // ─── SAVE MOOD ────────────────────────────────────────
        public static async Task SaveMoodAsync(
            string mood,
            int score,
            string notes,
            List<int> answers)
        {
            EnsureUser();

            int attemptNumber = await IncrementAttemptsAsync();

            await _db
                .Child("moods")
                .Child(CurrentUserId)
                .PostAsync(new MoodEntry
                {
                    UserId = CurrentUserId,
                    Mood = mood,
                    MoodScore = score,
                    Answers = answers,
                    AttemptNumber = attemptNumber,
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow.ToString("o")
                });

            await UpdateStreakAsync();
        }

        // ─── UPDATE STREAK ────────────────────────────────────
        public static async Task UpdateStreakAsync()
        {
            EnsureUser();

            try
            {
                var entries = await _db.Child("moods")
                    .Child(CurrentUserId)
                    .OnceAsync<MoodEntry>();

                var loggedDates = entries
                    .Select(e => {
                        DateTime.TryParse(e.Object.CreatedAt, out var d);
                        return d.Date;
                    })
                    .Where(d => d != default)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToList();

                if (loggedDates.Count == 0)
                {
                    await _db.Child("users").Child(CurrentUserId).Child("streak").PutAsync(0);
                    return;
                }

                var today = DateTime.Today;
                if (loggedDates[0] < today.AddDays(-1))
                {
                    await _db.Child("users").Child(CurrentUserId).Child("streak").PutAsync(0);
                    return;
                }

                int streak = 1;
                for (int i = 1; i < loggedDates.Count; i++)
                {
                    if ((loggedDates[i - 1] - loggedDates[i]).Days == 1)
                        streak++;
                    else
                        break;
                }

                await _db.Child("users").Child(CurrentUserId).Child("streak").PutAsync(streak);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UpdateStreakAsync error: {ex.Message}");
            }
        }

        // ─── GET MOODS ───────────────────────────────────────
        public static async Task<List<MoodEntry>> GetMoodsAsync()
        {
            EnsureUser();

            var entries = await _db
                .Child("moods")
                .Child(CurrentUserId)
                .OnceAsync<MoodEntry>();

            return entries
                .Select(e => e.Object)
                .OrderByDescending(e => e.CreatedAt)
                .ToList();
        }

        // ─── RESET PASSWORD ──────────────────────────────────
        public static async Task SendPasswordResetAsync(string email)
        {
            await _auth.ResetEmailPasswordAsync(email);
        }

        // ─── COPING PROGRESS ─────────────────────────────────
        public static async Task SaveCopingProgressAsync(int completed, int total)
        {
            EnsureUser();

            await _db.Child("users")
                .Child(CurrentUserId)
                .Child("copingProgress")
                .PutAsync(new { completed, total });
        }

        public static async Task<(int completed, int total)> GetCopingProgressAsync()
        {
            EnsureUser();

            try
            {
                var result = await _db.Child("users")
                    .Child(CurrentUserId)
                    .Child("copingProgress")
                    .OnceSingleAsync<dynamic>();

                if (result == null)
                    return (0, 4);

                int completed = result.completed;
                int total = result.total;

                return (completed, total);
            }
            catch
            {
                // New account — no data yet
                return (0, 4);
            }
        }

        // ─── CHANGE PASSWORD ──────────────────────────────────
        public static async Task ChangePasswordAsync(string currentPassword, string newPassword)
        {
            EnsureUser();

            var result = await _auth
                .SignInWithEmailAndPasswordAsync(CurrentUserEmail, currentPassword);

            await result.User.ChangePasswordAsync(newPassword);
        }

        // ─── HAS LOGGED MOOD TODAY ────────────────────────────
        public static async Task<bool> HasLoggedMoodTodayAsync()
        {
            EnsureUser();

            try
            {
                var today = DateTime.Today;

                var entries = await _db.Child("moods")
                    .Child(CurrentUserId)
                    .OnceAsync<MoodEntry>();

                return entries.Any(e =>
                    DateTime.TryParse(e.Object.CreatedAt, out var d) &&
                    d.Date == today);
            }
            catch
            {
                // New account — no mood logs yet
                return false;
            }
        }

        // ─── GET USER PROFILE ─────────────────────────────────
        public static async Task<UserProfile> GetUserProfileAsync()
        {
            EnsureUser();

            try
            {
                return await _db.Child("users")
                    .Child(CurrentUserId)
                    .OnceSingleAsync<UserProfile>();
            }
            catch
            {
                return null;
            }
        }

        // ─── RESET COPING IF NEW WEEK ─────────────────────────
        public static async Task ResetCopingProgressIfNewWeekAsync()
        {
            EnsureUser();

            // Guard: skip if user ID is missing
            if (string.IsNullOrEmpty(CurrentUserId)) return;

            int daysSinceMonday = ((int)DateTime.Today.DayOfWeek + 6) % 7;
            var weekStart = DateTime.Today.AddDays(-daysSinceMonday);
            string weekKey = weekStart.ToString("yyyy-MM-dd");

            try
            {
                var lastReset = await _db.Child("users")
                    .Child(CurrentUserId)
                    .Child("lastCopingReset")
                    .OnceSingleAsync<string>();

                // New account — no lastCopingReset yet
                if (lastReset == null)
                {
                    await _db.Child("users")
                        .Child(CurrentUserId)
                        .Child("lastCopingReset")
                        .PutAsync(weekKey);
                    return;
                }

                if (lastReset != weekKey)
                {
                    await SaveCopingProgressAsync(0, 4);

                    await _db.Child("users")
                        .Child(CurrentUserId)
                        .Child("lastCopingReset")
                        .PutAsync(weekKey);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ResetCopingProgressIfNewWeekAsync error: {ex.Message}");
            }
        }

        // ─── WEEKLY MOOD COUNT ────────────────────────────────
        public static async Task<int> GetWeeklyMainMoodCountAsync()
        {
            EnsureUser();

            try
            {
                int daysSinceMonday = ((int)DateTime.Today.DayOfWeek + 6) % 7;
                var weekStart = DateTime.Today.AddDays(-daysSinceMonday);

                var entries = await _db.Child("moods")
                    .Child(CurrentUserId)
                    .OnceAsync<MoodEntry>();

                return entries.Count(e =>
                    DateTime.TryParse(e.Object.CreatedAt, out var d) &&
                    d.Date >= weekStart);
            }
            catch
            {
                // New account — no moods yet
                return 0;
            }
        }

        // ─── STREAK ───────────────────────────────────────────
        public static async Task<int> GetStreakAsync()
        {
            EnsureUser();

            try
            {
                var result = await _db.Child("users")
                    .Child(CurrentUserId)
                    .Child("streak")
                    .OnceSingleAsync<int?>();

                return result ?? 0;
            }
            catch
            {
                // New account — no streak yet
                return 0;
            }
        }
    }
}