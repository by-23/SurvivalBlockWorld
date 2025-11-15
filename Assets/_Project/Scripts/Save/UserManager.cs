using System;
using System.Threading.Tasks;
using UnityEngine;

public class UserManager
{
    private const string USER_ID_KEY = "UserId";
    private static string _cachedUserId;
    private static bool _isInitialized = false;

    public static string UserId
    {
        get
        {
            if (string.IsNullOrEmpty(_cachedUserId))
            {
                _cachedUserId = PlayerPrefs.GetString(USER_ID_KEY, string.Empty);
            }

            return _cachedUserId;
        }
        private set
        {
            _cachedUserId = value;
            PlayerPrefs.SetString(USER_ID_KEY, value);
            PlayerPrefs.Save();
        }
    }

    public static bool IsInitialized => _isInitialized;

    public static async Task<string> InitializeUserIdAsync(FirebaseAdapter firebaseAdapter)
    {
        if (_isInitialized && !string.IsNullOrEmpty(UserId))
        {
            return UserId;
        }

        if (!string.IsNullOrEmpty(UserId))
        {
            _isInitialized = true;
            return UserId;
        }

        string newUserId = await GenerateAndRegisterUserIdAsync(firebaseAdapter);
        if (!string.IsNullOrEmpty(newUserId))
        {
            UserId = newUserId;
            _isInitialized = true;
            Debug.Log($"User ID initialized: {UserId}");
        }

        return UserId;
    }

    private static async Task<string> GenerateAndRegisterUserIdAsync(FirebaseAdapter firebaseAdapter)
    {
        const int maxAttempts = 10;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            string userId = GenerateUserId();

            bool isAvailable = await firebaseAdapter.IsUserIdAvailableAsync(userId);
            if (isAvailable)
            {
                bool registered = await firebaseAdapter.RegisterUserIdAsync(userId);
                if (registered)
                {
                    return userId;
                }
            }

            await Task.Delay(100);
        }

        Debug.LogError("Failed to generate unique user ID after multiple attempts");
        return string.Empty;
    }

    private static string GenerateUserId()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int random = UnityEngine.Random.Range(10000, 99999);
        return $"user_{timestamp}_{random}";
    }

    public static void Reset()
    {
        _cachedUserId = string.Empty;
        _isInitialized = false;
        PlayerPrefs.DeleteKey(USER_ID_KEY);
        PlayerPrefs.Save();
    }
}

