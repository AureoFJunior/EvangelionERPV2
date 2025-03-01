using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvangelionERPV2.Shared.Utils
{
    public static class SharedFunctions
    {
        private static readonly HttpClient _httpClient;
        private static IConfiguration _configuration;
        private static string _defaultApiUrl;
        private static string _encryptionKey = string.Empty;
        private static AWSKMSKeyProvider _kmsProvider;

        static SharedFunctions()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public static void Initialize(IServiceProvider serviceProvider)
        {
            if(serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            _configuration = serviceProvider.GetRequiredService<IConfiguration>();
            _kmsProvider = serviceProvider.GetRequiredService<AWSKMSKeyProvider>();
            _defaultApiUrl = _configuration.GetSection("HttpConfig")["DefaultApiUrl"] ?? string.Empty;
            _encryptionKey = _kmsProvider.GetKMSKey(_configuration.GetSection("Encryption")["Key"] ?? string.Empty);
        }

        #region Utils
        public static bool IsNotNullOrEmpty<T>(IEnumerable<T> enumerable)
        {
            return enumerable != null && enumerable.Any();
        }

        public static T SafeConvertToNumber<T>(string input) where T : struct
        {
            if (string.IsNullOrEmpty(input))
                return default;
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
            return default;
        }

        public static bool IsDateBetween(this DateTime input, DateTime start, DateTime end)
        {
            return input >= start && input <= end;
        }

        public static bool IsDateBetween(this DateTime? input, DateTime start, DateTime end)
        {
            return input >= start && input <= end;
        }

        public static bool IsLastMonthDay(this DateTime input)
        {
            return input == GetLastDayOfMonth();
        }

        public static DateTime GetFirstDayOfMonth()
        {
            DateTime now = DateTime.UtcNow;
            return new DateTime(now.Year, now.Month, 1);
        }

        public static DateTime GetLastDayOfMonth()
        {
            DateTime now = DateTime.UtcNow;
            DateTime lastDayOfMonth = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
            return lastDayOfMonth;
        }

        public static T ConvertObject<T>(object obj)
        {
            if (obj == null) return default;

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
        public static async Task<T?> GetAsync<T>(string apiEndpoint, string parameters = "", string token = "", string apiBaseUrl = "")
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBaseUrl}/{apiEndpoint}/{parameters}");

            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            return default(T);
        }

        public static async Task<T?> PostAsync<T>(string apiEndpoint, object resource, string token = "", string apiBaseUrl = "")
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBaseUrl}/{apiEndpoint}");

            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var serializedResource = JsonSerializer.Serialize(resource);
            request.Content = new StringContent(serializedResource, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            return default(T);
        }

        public static async Task<bool> PutAsync(string apiEndpoint, string parameters, object updatedResource, string token = "", string apiBaseUrl = "")
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var request = new HttpRequestMessage(HttpMethod.Put, $"{apiBaseUrl}/{apiEndpoint}/{parameters}");

            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var serializedResource = JsonSerializer.Serialize(updatedResource);
            request.Content = new StringContent(serializedResource, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        public static async Task<bool> DeleteAsync(string apiEndpoint, string parameters, string token = "", string apiBaseUrl = "")
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{apiBaseUrl}/{apiEndpoint}/{parameters}");

            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
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
                var key = Convert.FromBase64String(_encryptionKey);

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
                var key = Convert.FromBase64String(_encryptionKey);

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

        #region Extensions
        public static void CopyTo<T>(this T source, T destination) where T : class
        {
            if (source == null || destination == null)
                throw new ArgumentNullException();

            var properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
            foreach (var property in properties)
            {
                if (property.CanRead && property.CanWrite)
                {
                    var value = property.GetValue(source);
                    property.SetValue(destination, value);
                }
            }
        }

        public static string ClearString(this string source)
        {
            return Regex.Replace(source, "[^0-9a-zA-Z]+", "");
        }

        #endregion

        #region AWS S3

        public static async Task CreateItemAsync(this IAmazonS3 _s3Client, string bucketName, string key, Stream content)
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                InputStream = content
            };

            await _s3Client.PutObjectAsync(putRequest);
        }

        public static async Task<Stream> GetItemAsync(this IAmazonS3 _s3Client, string bucketName, string key)
        {
            var getRequest = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };

            using (var response = await _s3Client.GetObjectAsync(getRequest))
            {
                var responseStream = new MemoryStream();
                await response.ResponseStream.CopyToAsync(responseStream);
                responseStream.Position = 0;  // Reset the stream position to the beginning
                return responseStream;
            }
        }

        public static async Task DeleteItemAsync(this IAmazonS3 _s3Client, string bucketName, string key)
        {
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };

            await _s3Client.DeleteObjectAsync(deleteRequest);
        }

        #endregion
    }
}
