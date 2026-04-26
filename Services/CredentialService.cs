using System;
using System.IO;
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
                    var parts = File.ReadAllText(_credFilePath).Split('|');
                    if (parts.Length == 2)
                    {
                        string user = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                        string pass = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                        
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
                    string encUser = Convert.ToBase64String(Encoding.UTF8.GetBytes(username));
                    string encPass = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
                    File.WriteAllText(_credFilePath, $"{encUser}|{encPass}");
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