using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AdminInfoTools.Services
{
    public class CredentialService
    {
        public string Username { get; private set; }
        public string Password { get; private set; }
        public bool AreCredentialsSet => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

        private readonly string _credFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials.dat");

        public (string Username, string Password)? LoadSavedCredentials()
        {
            try
            {
                if (File.Exists(_credFilePath))
                {
                    byte[] encryptedData = File.ReadAllBytes(_credFilePath);
                    byte[] decryptedData = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
                    var parts = Encoding.UTF8.GetString(decryptedData).Split('|');

                    if (parts.Length == 2)
                    {
                        string user = parts[0];
                        string pass = parts[1];
                        SetSessionCredentials(user, pass);

                        return (user, pass);
                    }
                }
            }
            catch
            {
                if (File.Exists(_credFilePath))
                {
                    File.Delete(_credFilePath);
                }
            }
            return null;
        }

        public void SetSessionCredentials(string username, string password)
        {
            Username = username;
            Password = password;
        }

        public void SaveOrClearCredentials(string username, string password, bool remember)
        {
            SetSessionCredentials(username, password);

            try
            {
                if (remember)
                {
                    string credentialsToSave = $"{username}|{password}";
                    byte[] dataToEncrypt = Encoding.UTF8.GetBytes(credentialsToSave);
                    byte[] encryptedData = ProtectedData.Protect(dataToEncrypt, null, DataProtectionScope.CurrentUser);

                    File.WriteAllBytes(_credFilePath, encryptedData);
                }
                else if (File.Exists(_credFilePath))
                {
                    File.Delete(_credFilePath);
                }
            }
            catch (Exception ex)
            {
                throw new IOException("Failed to save credentials to disk.", ex);
            }
        }
    }
}