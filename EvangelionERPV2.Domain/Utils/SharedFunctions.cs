﻿using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace EvangelionERPV2.Domain.Utils
{
    public static class SharedFunctions
    {
        private static readonly HttpClient _httpClient;
        private static readonly IConfiguration _configuration;
        private static string _defaultApiUrl;

        static SharedFunctions()
        {
            _httpClient = new HttpClient();
            _configuration = new ConfigurationBuilder().Build();
            _defaultApiUrl = _configuration.GetSection("HttpConfig")["DefaultApiUrl"];
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
    }
}