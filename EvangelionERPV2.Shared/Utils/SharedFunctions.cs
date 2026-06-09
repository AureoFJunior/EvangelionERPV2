using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Net;
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
        private static IConfiguration _configuration = null!;
        private static string _defaultApiUrl = string.Empty;
        private static string _encryptionKey = string.Empty;
        private static AWSKMSKeyProvider _kmsProvider = null!;

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
            else if (typeof(T) == typeof(uint))
            {
                if (uint.TryParse(input, out uint result))
                    return (T)(object)result;
            }
            else if (typeof(T) == typeof(short))
            {
                if (short.TryParse(input, out short result))
                    return (T)(object)result;
            }
            else if (typeof(T) == typeof(ushort))
            {
                if (ushort.TryParse(input, out ushort result))
                    return (T)(object)result;
            }
            else if (typeof(T) == typeof(long))
            {
                if (long.TryParse(input, out long result))
                    return (T)(object)result;
            }
            else if (typeof(T) == typeof(ulong))
            {
                if (ulong.TryParse(input, out ulong result))
                    return (T)(object)result;
            }
            else if (typeof(T) == typeof(float))
            {
                if (float.TryParse(input, out float result))
                    return (T)(object)result;
            }
            else if (typeof(T) == typeof(double))
            {
                if (double.TryParse(input, out double result))
                    return (T)(object)result;
            }
            else if (typeof(T) == typeof(decimal))
            {
                if (decimal.TryParse(input, out decimal result))
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
            return input.HasValue && input.Value >= start && input.Value <= end;
        }

        public static bool IsLastMonthDay(this DateTime input)
        {
            var lastDay = GetLastDayOfMonth();
            return input.Date == lastDay.Date;
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

        public static T? ConvertObject<T>(object? obj)
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
                object? value = property.GetValue(obj);
                if (value != null)
                {
                    string fieldName = property.Name;
                    string fieldValue = value.ToString() ?? string.Empty;
                    fieldValues.Add(fieldName, fieldValue);
                }
            }

            return fieldValues;
        }

        public static string EnsureEncryptedAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return string.Empty;

            var trimmed = address.Trim();
            var decrypted = Decrypt(trimmed);
            if (!string.IsNullOrWhiteSpace(decrypted))
            {
                if (!TryNormalizeStorageObjectKey(decrypted, out _))
                    return string.Empty;

                return trimmed;
            }

            if (!TryNormalizeStorageObjectKey(trimmed, out var normalizedKey))
                return string.Empty;

            var encryptedValue = Encrypt(normalizedKey);
            return !string.IsNullOrWhiteSpace(encryptedValue)
                ? encryptedValue
                : normalizedKey;
        }

        public static string EnsureDecryptedAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return string.Empty;

            var trimmed = address.Trim();
            var decrypted = Decrypt(trimmed);
            var candidate = !string.IsNullOrWhiteSpace(decrypted) ? decrypted : trimmed;

            return TryNormalizeStorageObjectKey(candidate, out var normalizedKey)
                ? normalizedKey
                : string.Empty;
        }

        public static string NormalizeBase64Payload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return string.Empty;

            var trimmed = payload.Trim();
            var markerIndex = trimmed.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && markerIndex >= 0)
                return trimmed.Substring(markerIndex + "base64,".Length);

            return trimmed;
        }

        public static MemoryStream GetMemoryStreamFromBase64Payload(string payload, long maxBytes = 5 * 1024 * 1024)
        {
            var normalizedPayload = NormalizeBase64Payload(payload);
            if (!TryGetBase64DecodedByteCount(normalizedPayload, out var decodedByteCount))
                throw new ArgumentException("Payload must be a valid base64 value.", nameof(payload));

            if (maxBytes > 0 && decodedByteCount > maxBytes)
                throw new ArgumentException($"Payload must be {maxBytes} bytes or smaller after decoding.", nameof(payload));

            var bytes = Convert.FromBase64String(normalizedPayload);
            return new MemoryStream(bytes);
        }

        public static string GetAWSSetting(IConfiguration configuration, string settingName)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (string.IsNullOrWhiteSpace(settingName))
                return string.Empty;

            return configuration.GetSection("AWSSettings")[settingName] ?? string.Empty;
        }

        public static string GetProductBucketName(IConfiguration configuration)
        {
            var bucketName = GetAWSSetting(configuration, "BucketProducttName");
            if (!string.IsNullOrWhiteSpace(bucketName))
                return bucketName;

            return GetAWSSetting(configuration, "BucketProductName");
        }

        public static string GetUserBucketName(IConfiguration configuration)
        {
            var userBucket = GetAWSSetting(configuration, "BucketUserName");
            if (!string.IsNullOrWhiteSpace(userBucket))
                return userBucket;

            return GetProductBucketName(configuration);
        }

        public static RegionEndpoint GetAWSRegion(IConfiguration configuration)
        {
            var configuredRegion = GetAWSSetting(configuration, "Region");
            if (!string.IsNullOrWhiteSpace(configuredRegion))
                return RegionEndpoint.GetBySystemName(configuredRegion.Trim());

            var environmentRegion =
                Environment.GetEnvironmentVariable("AWS_REGION")
                ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");

            if (!string.IsNullOrWhiteSpace(environmentRegion))
                return RegionEndpoint.GetBySystemName(environmentRegion.Trim());

            return RegionEndpoint.USEast1;
        }

        public static string GenerateStorageObjectKey(string prefix, Guid entityId)
        {
            var normalizedPrefix = Regex.Replace(
                (prefix ?? string.Empty).Trim().ToLowerInvariant(),
                "[^a-z0-9/_-]+",
                string.Empty,
                RegexOptions.CultureInvariant)
                .Trim('/');

            if (string.IsNullOrWhiteSpace(normalizedPrefix))
                normalizedPrefix = "files";

            var entitySegment = entityId != Guid.Empty
                ? $"{entityId:N}-"
                : string.Empty;

            return $"{normalizedPrefix}/{DateTime.UtcNow:yyyyMMddHHmmssfff}-{entitySegment}{Guid.NewGuid():N}";
        }

        public static string EscapeLikePattern(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal)
                .Replace("[", "\\[", StringComparison.Ordinal);
        }

        public static string EnsureSearchFilterLength(string? value, int maxLength, string parameterName)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (maxLength > 0 && trimmed.Length > maxLength)
                throw new ArgumentException($"{parameterName} filter must be {maxLength} characters or fewer.", parameterName);

            return trimmed;
        }

        private static bool TryGetBase64DecodedByteCount(string normalizedPayload, out long decodedByteCount)
        {
            decodedByteCount = 0;

            if (string.IsNullOrWhiteSpace(normalizedPayload))
                return false;

            var length = 0L;
            var paddingCount = 0L;
            var seenPadding = false;

            foreach (var character in normalizedPayload)
            {
                if (char.IsWhiteSpace(character))
                    continue;

                if (character == '=')
                {
                    seenPadding = true;
                    paddingCount++;
                    length++;

                    if (paddingCount > 2)
                        return false;

                    continue;
                }

                if (seenPadding || !IsBase64Character(character))
                    return false;

                length++;
            }

            if (length == 0 || length % 4 != 0)
                return false;

            decodedByteCount = ((length / 4) * 3) - paddingCount;
            return decodedByteCount > 0;
        }

        private static bool IsBase64Character(char value)
        {
            return (value >= 'A' && value <= 'Z') ||
                   (value >= 'a' && value <= 'z') ||
                   (value >= '0' && value <= '9') ||
                   value == '+' ||
                   value == '/';
        }

        #endregion

        #region HTTP/HTTPS
        public static async Task<T?> GetAsync<T>(string apiEndpoint, string parameters = "", string token = "", string apiBaseUrl = "", CancellationToken cancellationToken = default)
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(apiBaseUrl, apiEndpoint, parameters));

            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            Log.Logger.Warning(
                "HTTP GET request failed. Endpoint={Endpoint} StatusCode={StatusCode}",
                apiEndpoint,
                (int)response.StatusCode);

            return default(T);
        }

        public static async Task<T?> PostAsync<T>(string apiEndpoint, object resource, string token = "", string apiBaseUrl = "", CancellationToken cancellationToken = default)
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var request = new HttpRequestMessage(HttpMethod.Post, BuildRequestUri(apiBaseUrl, apiEndpoint));

            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var serializedResource = JsonSerializer.Serialize(resource);
            request.Content = new StringContent(serializedResource, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            Log.Logger.Warning(
                "HTTP POST request failed. Endpoint={Endpoint} StatusCode={StatusCode}",
                apiEndpoint,
                (int)response.StatusCode);

            return default(T);
        }

        public static async Task<bool> PutAsync(string apiEndpoint, string parameters, object updatedResource, string token = "", string apiBaseUrl = "", CancellationToken cancellationToken = default)
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var request = new HttpRequestMessage(HttpMethod.Put, BuildRequestUri(apiBaseUrl, apiEndpoint, parameters));

            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var serializedResource = JsonSerializer.Serialize(updatedResource);
            request.Content = new StringContent(serializedResource, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }

        public static async Task<bool> DeleteAsync(string apiEndpoint, string parameters, string token = "", string apiBaseUrl = "", CancellationToken cancellationToken = default)
        {
            apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? _defaultApiUrl : apiBaseUrl;
            var request = new HttpRequestMessage(HttpMethod.Delete, BuildRequestUri(apiBaseUrl, apiEndpoint, parameters));

            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, cancellationToken);
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

        private static Uri BuildRequestUri(string apiBaseUrl, string apiEndpoint, string? parameters = null)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
                throw new InvalidOperationException("API base URL is not configured.");

            if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var baseUri) ||
                !(baseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                  baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("API base URL must be an absolute HTTP or HTTPS URI.", nameof(apiBaseUrl));
            }

            var segments = new List<string>();
            AppendPathSegments(segments, apiEndpoint, nameof(apiEndpoint));
            AppendPathSegments(segments, parameters, nameof(parameters));

            var normalizedBase = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? baseUri.AbsoluteUri
                : $"{baseUri.AbsoluteUri}/";

            return segments.Count == 0
                ? new Uri(normalizedBase, UriKind.Absolute)
                : new Uri(new Uri(normalizedBase, UriKind.Absolute), string.Join("/", segments));
        }

        private static void AppendPathSegments(ICollection<string> segments, string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (var rawSegment in value.Split('/', StringSplitOptions.None))
            {
                var segment = rawSegment.Trim();
                if (segment.Length == 0)
                    throw new ArgumentException("API path segments cannot be empty.", parameterName);

                if (segment is "." or "..")
                    throw new ArgumentException("API path segments cannot contain traversal markers.", parameterName);

                segments.Add(Uri.EscapeDataString(segment));
            }
        }

        #region Encryption
        private const int PasswordSaltSize = 16;
        private const int PasswordKeySize = 32;
        private const int PasswordIterations = 100_000;
        private const string PasswordHashPrefix = "PBKDF2";

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
            catch (Exception)
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
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static string GetEncryptionKey()
        {
            if (_configuration == null || _kmsProvider == null)
                throw new InvalidOperationException("SharedFunctions.Initialize must be called before accessing encryption settings.");

            return _kmsProvider.GetKMSKey(_configuration.GetSection("Encryption")["TokenKey"] ?? string.Empty);
        }
        #endregion

        #region Password Hashing
        public static string HashPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return string.Empty;

            var salt = RandomNumberGenerator.GetBytes(PasswordSaltSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, PasswordKeySize);
            return $"{PasswordHashPrefix}${PasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        public static bool VerifyPassword(string password, string storedHash, out bool needsRehash)
        {
            needsRehash = false;

            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
                return false;

            if (IsPasswordHashFormat(storedHash))
            {
                var parts = storedHash.Split('$');
                if (parts.Length != 4)
                    return false;

                if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
                    return false;

                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

                return FixedTimeEquals(actual, expected);
            }

            var legacy = Decrypt(storedHash);
            if (string.IsNullOrEmpty(legacy))
                return false;

            var legacyMatch = FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(legacy));
            if (legacyMatch)
                needsRehash = true;

            return legacyMatch;
        }

        public static bool IsPasswordHashFormat(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.StartsWith($"{PasswordHashPrefix}$", StringComparison.Ordinal);
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(left, right);
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
            if (!TryNormalizeStorageObjectKey(key, out var normalizedKey))
                throw new ArgumentException("Storage object key is invalid.", nameof(key));

            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = normalizedKey,
                InputStream = content
            };

            await _s3Client.PutObjectAsync(putRequest);
        }

        public static async Task<Stream> GetItemAsync(this IAmazonS3 _s3Client, string bucketName, string key)
        {
            if (!TryNormalizeStorageObjectKey(key, out var normalizedKey))
                throw new ArgumentException("Storage object key is invalid.", nameof(key));

            var getRequest = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = normalizedKey
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
            if (!TryNormalizeStorageObjectKey(key, out var normalizedKey))
                throw new ArgumentException("Storage object key is invalid.", nameof(key));

            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = normalizedKey
            };

            await _s3Client.DeleteObjectAsync(deleteRequest);
        }


        public static async Task DeleteItemIfExistsAsync(this IAmazonS3 _s3Client, string bucketName, string? encryptedOrPlainKey)
        {
            if (string.IsNullOrWhiteSpace(bucketName))
                return;

            var key = EnsureDecryptedAddress(encryptedOrPlainKey);
            if (string.IsNullOrWhiteSpace(key))
                return;

            try
            {
                await _s3Client.DeleteItemAsync(bucketName, key);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadRequest)
            {
                // Ignore not found/legacy key formats so replace flows can continue.
            }
        }

        public static async Task<string> GetItemBase64Async(this IAmazonS3 _s3Client, string bucketName, string? encryptedOrPlainKey)
        {
            if (string.IsNullOrWhiteSpace(bucketName))
                return string.Empty;

            var key = EnsureDecryptedAddress(encryptedOrPlainKey);
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            using var itemStream = await _s3Client.GetItemAsync(bucketName, key);
            using var buffer = new MemoryStream();
            await itemStream.CopyToAsync(buffer);
            return Convert.ToBase64String(buffer.ToArray());
        }

        private static bool TryNormalizeStorageObjectKey(string? value, out string normalizedKey)
        {
            normalizedKey = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 1024)
                return false;

            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("base64,", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("://", StringComparison.Ordinal) ||
                trimmed.Contains('\\') ||
                trimmed.Contains(':') ||
                trimmed.Contains("..", StringComparison.Ordinal) ||
                trimmed.StartsWith("/", StringComparison.Ordinal) ||
                trimmed.EndsWith("/", StringComparison.Ordinal) ||
                trimmed.Contains("//", StringComparison.Ordinal))
            {
                return false;
            }

            if (!Regex.IsMatch(trimmed, "^[A-Za-z0-9][A-Za-z0-9/_.-]{0,1023}$", RegexOptions.CultureInvariant))
                return false;

            normalizedKey = trimmed;
            return true;
        }

        #endregion
    }
}
