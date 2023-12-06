using SnowplowTracker;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using System;

namespace RuzdAnalytics
{
    public class APIClient
    {
        private Analytics analyticsInstance;
        private string apiEndpoint;
        private string identifier;

        public static readonly string DEFAULT_API_ENDPOINT = "https://harbor.ruzd.net";

        public APIClient(Analytics analyticsInstance, string apiEndpoint, string identifier)
        {
            this.analyticsInstance = analyticsInstance;
            this.apiEndpoint = apiEndpoint;
            this.identifier = identifier;
        }

        public async Task<RemoteTrackingConfig> GetRemoteTrackingConfig()
        {
            string url = apiEndpoint + "/v0/game/" + identifier + "/config";
            // add query parameters
            url += "?sdk=" + analyticsInstance.GetRuzdVersionIdentifier();
            url += "&build=" + analyticsInstance.GetBuildVersion();
            url += "&user_id=" + analyticsInstance.GetPlayerId();

            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    return RemoteTrackingConfig.FromJson(result);
                }
            }
            return null;
        }

        public async Task<bool> PostFeedback(Feedback feedbackEvent)
        {
            string url = apiEndpoint + "/v0/game/" + identifier + "/feedback";
            // add query parameters
            url += "?sdk=" + analyticsInstance.GetRuzdVersionIdentifier();
            url += "&build=" + analyticsInstance.GetBuildVersion();
            using (var client = new HttpClient())
            {
                StringContent content = new StringContent(JsonConvert.SerializeObject(feedbackEvent));
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                string raw_json = JsonConvert.SerializeObject(feedbackEvent);
                var response = await client.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    Log.Debug($"[RuzdAnalytics] Feedback posted with result: {result}");
                    return true;
                }
                else
                {
                    Log.Error($"[RuzdAnalytics] Feedback post failed with status code: {response.StatusCode}");
                    var errorMessages = await response.Content.ReadAsStringAsync();
                }
            }
            return false;
        }
    }

    public class Feedback
    {
        public int rating { get; set; }
        public string user_id { get; set; }
        public string message { get; set; }
        public Dictionary<string, string> context { get; set; } = new Dictionary<string, string>();
        public Feedback(string user_id, int rating, string message, Dictionary<string, string> extra = null)
        {
            this.rating = rating;
            this.message = message;
            this.user_id = user_id;
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> entry in extra)
                {
                    this.context.Add(entry.Key, entry.Value);
                }
            }
        }
    }

    public class RemoteTrackingConfig
    {
        public bool enabled = false;
        public TrackingLevel level;
        public string trackingEndpoint;
        public bool valid = false;

        public RemoteTrackingConfig(bool valid, bool enabled, TrackingLevel level, string trackingEndpoint)
        {
            this.valid = valid;
            this.enabled = enabled;
            this.level = level;
            this.trackingEndpoint = trackingEndpoint;
        }

        public static RemoteTrackingConfig FromJson(string json)
        {
            bool tracking_enabled = false;
            TrackingLevel tracking_level = TrackingLevel.NORMAL;
            string tracking_endpoint = null;
            bool valid = false;

            RemoteConfig config = JsonConvert.DeserializeObject<RemoteConfig>(json);
            if (config != null)
            {
                Log.Debug("[RuzdAnalytics] Parsing ruzd remote tracking config");
                // Check if tracking is enabled
                Nullable<bool> raw_tracking_enabled = config.GetBooleanValue("tracking", namespaceFilter: "ruzd");
                if (raw_tracking_enabled == null)
                {
                    Log.Warning("[RuzdAnalytics] Not able to read tracking config");
                }
                else
                {
                    tracking_enabled = raw_tracking_enabled.Value;
                    valid = true;
                }

                // Get tracking level
                int raw_tracking_level = config.GetIntegerValue("tracking_level", (int)TrackingLevel.NORMAL, namespaceFilter: "ruzd");
                tracking_level = (TrackingLevel)raw_tracking_level;

                // Get tracking endpoint
                tracking_endpoint = config.GetStringValue("tracking_endpoint", namespaceFilter: "ruzd");
            }
            else
            {
                Log.Warning("[RuzdAnalytics] Not able to parse ruzd remote tracking config");
            }
            return new RemoteTrackingConfig(valid, tracking_enabled, tracking_level, tracking_endpoint);
        }
    }

    public class RemoteConfig
    {
        public string id { get; set; }
        public List<Namespace> namespaces { get; set; }

        public ConfigAttribute GetValue(string key, string namespaceFilter = "default")
        {
            foreach (Namespace ns in namespaces)
            {
                if (ns.name == namespaceFilter)
                {
                    foreach (KeyValuePair<string, ConfigAttribute> entry in ns.config)
                    {
                        if (entry.Key == key)
                        {
                            return entry.Value;
                        }
                    }
                }
            }
            return null;
        }

        public Nullable<bool> GetBooleanValue(string key, string namespaceFilter = "default")
        {
            ConfigAttribute attr = GetValue(key, namespaceFilter);
            return attr.getBoolean();
        }

        public bool GetBooleanValue(string key, bool defaultValue, string namespaceFilter = "default")
        {
            Nullable<bool> raw_bool = GetBooleanValue(key, namespaceFilter);
            if (raw_bool != null)
            {
                return raw_bool.Value;
            }
            return defaultValue;
        }

        public Nullable<int> GetIntegerValue(string key, string namespaceFilter = "default")
        {
            ConfigAttribute attr = GetValue(key, namespaceFilter);
            return attr.getInteger();
        }

        public int GetIntegerValue(string key, int defaultValue, string namespaceFilter = "default")
        {
            Nullable<int> raw_int = GetIntegerValue(key, namespaceFilter);
            if (raw_int != null)
            {
                return raw_int.Value;
            }
            return defaultValue;
        }

        public Nullable<double> GetDoubleValue(string key, string namespaceFilter = "default")
        {
            ConfigAttribute attr = GetValue(key, namespaceFilter);
            return attr.getDouble();
        }

        public double GetDoubleValue(string key, double defaultValue, string namespaceFilter = "default")
        {
            Nullable<double> raw_double = GetDoubleValue(key, namespaceFilter);
            if (raw_double != null)
            {
                return raw_double.Value;
            }
            return defaultValue;
        }

        public string GetStringValue(string key, string namespaceFilter = "default")
        {
            ConfigAttribute attr = GetValue(key, namespaceFilter);
            return attr.getString();
        }

        public string GetStringValue(string key, string defaultValue, string namespaceFilter = "default")
        {
            string raw_string = GetStringValue(key, namespaceFilter);
            if (raw_string != null)
            {
                return raw_string;
            }
            return defaultValue;
        }
    }

    public class Namespace
    {
        public string name { get; set; }
        public Dictionary<string, ConfigAttribute> config { get; set; }
    }

    public class ConfigAttribute
    {
        public string type { get; set; }
        public object value { get; set; }

        public Nullable<bool> getBoolean()
        {
            if (type == "boolean")
            {
                return (bool)value;
            }
            return null;
        }

        public Nullable<int> getInteger()
        {
            if (type == "integer")
            {
                return Convert.ToInt32(value);

            }
            return null;
        }

        public Nullable<double> getDouble()
        {
            if (type == "number")
            {
                return (double)value;
            }
            return null;
        }

        public string getString()
        {
            if (type == "string")
            {
                return (string)value;
            }
            return null;
        }
    }
}
