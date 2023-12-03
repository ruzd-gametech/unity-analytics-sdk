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
        protected string apiEndpoint;
        protected string identifier;

        public static readonly string DEFAULT_API_ENDPOINT = "https://harbor.ruzd.net";

        public APIClient(string apiEndpoint, string identifier)
        {
            this.apiEndpoint = apiEndpoint;
            this.identifier = identifier;
        }

        public async Task<RemoteTrackingConfig> GetRemoteTrackingConfig()
        {
            string url = apiEndpoint + "/v0/game/" + identifier + "/config";
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
            foreach (Namespace ns in config.namespaces)
            {
                if (ns.name == "ruzd")
                {
                    Log.Debug("Got ruzd remote tracking config");
                    foreach (KeyValuePair<string, ConfigAttribute> entry in ns.config)
                    {
                        // check enabled
                        if (entry.Key == "tracking")
                        {
                            var raw_tracking_enabled = entry.Value.getBoolean();
                            if (raw_tracking_enabled == null)
                            {
                                Log.Warning("Not able to read tracking config");
                            }
                            else
                            {
                                tracking_enabled = raw_tracking_enabled.Value;
                                valid = true;
                            }
                        }

                        // check level
                        if (entry.Key == "tracking_level")
                        {
                            var raw_tracking_level = entry.Value.getInteger();
                            if (raw_tracking_level == null)
                            {
                                Log.Warning("Not able to read tracking level");
                            }
                            else
                            {
                                tracking_level = (TrackingLevel)raw_tracking_level.Value;
                            }
                        }

                        // check endpoint
                        if (entry.Key == "tracking_endpoint")
                        {
                            var raw_tracking_endpoint = entry.Value.getString();
                            if (string.IsNullOrEmpty(raw_tracking_endpoint))
                            {
                                Log.Warning("Not able to read tracking endpoint");
                            }
                            else
                            {
                                tracking_endpoint = raw_tracking_endpoint;
                            }
                        }
                    }
                }
            }
            return new RemoteTrackingConfig(valid, tracking_enabled, tracking_level, tracking_endpoint);
        }
    }

    public class RemoteConfig
    {
        public string id { get; set; }
        public List<Namespace> namespaces { get; set; }
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
