using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvangelionERPV2.Domain.Utils
{
    public static class SharedFunctions
    {
        private static readonly HttpClient _httpClient;
        private static readonly IConfiguration _configuration;
        private static string _defaultApiUrl;
        private static string _encryptionKey = string.Empty;

        static SharedFunctions()
        {
            _httpClient = new HttpClient();
            _configuration = new ConfigurationBuilder().Build();
            _defaultApiUrl = _configuration.GetSection("HttpConfig")["DefaultApiUrl"];
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _encryptionKey = _configuration.GetSection("Encryption")["Key"];
        }

       

        #region Utils
        public static bool IsNotNullOrEmpty<T>(IEnumerable<T> enumerable)
        {
            return enumerable != null && enumerable.Any();
        }

        public static T SafeConvertToNumber<T>(string input) where T : struct
        {
            if (string.IsNullOrEmpty(input))
                return default(T);
            if (typeof(T) == typeof(int))
            {
                if (int.TryParse(input, out int result))
                    return (T)(object)result;
            }
            else if (typeof(T) == typeof(short))
            {
                if (short.TryParse(input, out short result))
                    return (T)(object)result;
            }
            else if (typeof(T) == typeof(long))
            {
                if (long.TryParse(input, out long result))
                    return (T)(object)result;
            }
            return default(T);
        }

        public static bool IsDateBetween(DateTime input, DateTime start, DateTime end)
        {
            return input >= start && input <= end;
        }

        public static T ConvertObject<T>(object obj)
        {
            if (obj == null) return default(T);

            var json = JsonSerializer.Serialize(obj);
            return JsonSerializer.Deserialize<T>(json);
        }

        public static T IsNullOrZero<T>(this T variable, T defaultValue)
        {
            if (defaultValue == null) throw new ArgumentException("default value can't be null", "defaultValue");
            if (variable == null || variable.Equals(default(T)))
                return defaultValue;
            return variable;
        }

        public static Dictionary<string, string> GetFieldValues<T>(T obj)
        {
            Dictionary<string, string> fieldValues = new Dictionary<string, string>();

            PropertyInfo[] properties = typeof(T).GetProperties();

            foreach (PropertyInfo property in properties)
            {
                object value = property.GetValue(obj);
                if (value != null)
                {
                    string fieldName = property.Name;
                    string fieldValue = value.ToString();
                    fieldValues.Add(fieldName, fieldValue);
                }
            }

            return fieldValues;
        }

        #endregion

        #region HTTP/HTTPS
        public static async Task<object> GetAsync(string apiEndpoint, string parameters = "", string token = "", string apiBaseUrl = "")
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBaseUrl}/{apiEndpoint}/{parameters}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);


            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<object>(content);
            }

            return null;
        }

        public static async Task<object> PostAsync(string apiEndpoint, object resource, string apiBaseUrl = "")
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var serializedResource = JsonSerializer.Serialize(resource);
            var content = new StringContent(serializedResource, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{apiBaseUrl}/{apiEndpoint}", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<object>(responseContent);
            }

            return null;
        }

        public static async Task<bool> PutAsync(string apiEndpoint, string parameters, object updatedResource, string apiBaseUrl = "")
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var serializedResource = JsonSerializer.Serialize(updatedResource);
            var content = new StringContent(serializedResource, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync($"{apiBaseUrl}/{apiEndpoint}/{parameters}", content);

            return response.IsSuccessStatusCode;
        }

        public static async Task<bool> DeleteAsync(string apiEndpoint, string parameters, string apiBaseUrl = "")
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var response = await _httpClient.DeleteAsync($"{apiBaseUrl}/{apiEndpoint}/{parameters}");

            return response.IsSuccessStatusCode;
        }

        #endregion

        #region Email

        public static async Task<bool> IsEmailValid<T>(string email)
        {
            string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";

            // Use Regex.IsMatch to check if the email matches the pattern
            return Regex.IsMatch(email, pattern);
        }

        #endregion

        #region Encryption

        public static string Encrypt(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            try
            {
                var key = Encoding.UTF8.GetBytes(_encryptionKey);

                using (var aesAlg = Aes.Create())
                {
                    using (var encryptor = aesAlg.CreateEncryptor(key, aesAlg.IV))
                    {
                        using (var msEncrypt = new MemoryStream())
                        {
                            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                            using (var swEncrypt = new StreamWriter(csEncrypt))
                            {
                                swEncrypt.Write(value);
                            }

                            var iv = aesAlg.IV;

                            var decryptedContent = msEncrypt.ToArray();

                            var result = new byte[iv.Length + decryptedContent.Length];

                            Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
                            Buffer.BlockCopy(decryptedContent, 0, result, iv.Length, decryptedContent.Length);

                            var str = Convert.ToBase64String(result);
                            var fullCipher = Convert.FromBase64String(str);
                            return str;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return string.Empty;
            }
        }

        public static string Decrypt(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            try
            {
                value = value.Replace(" ", "+");
                var fullCipher = Convert.FromBase64String(value);

                var iv = new byte[16];
                var cipher = new byte[fullCipher.Length - iv.Length];

                Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, fullCipher.Length - iv.Length);
                var key = Encoding.UTF8.GetBytes(_encryptionKey);

                using (var aesAlg = Aes.Create())
                {
                    using (var decryptor = aesAlg.CreateDecryptor(key, iv))
                    {
                        string result;
                        using (var msDecrypt = new MemoryStream(cipher))
                        {
                            using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                            {
                                using (var srDecrypt = new StreamReader(csDecrypt))
                                {
                                    result = srDecrypt.ReadToEnd();
                                }
                            }
                        }

                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return string.Empty;
            }
        }
        #endregion
    }
}
