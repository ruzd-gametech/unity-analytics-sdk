using SnowplowTracker.Payloads;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using SnowplowTracker;
using SnowplowTracker.Payloads.Contexts;
using SnowplowTracker.Events;

namespace RuzdAnalytics
{
    public class RuzdEventSchemas
    {
        public readonly static string gameEvent = "iglu:com.ruzd/gameEvent/jsonschema/1-0-0";
        public readonly static string progressEvent = "iglu:com.ruzd/progressEvent/jsonschema/1-0-0";
        public readonly static string resourceEvent = "iglu:com.ruzd/resourceEvent/jsonschema/1-0-0";
        public readonly static string errorEvent = "iglu:com.ruzd/errorEvent/jsonschema/1-0-0";
        public readonly static string customEvent = "iglu:com.ruzd/customEvent/jsonschema/1-0-0";

        public readonly static int customEventMaxSize = 512;
    }

    public class RuzdEvent
    {
        protected Dictionary<string, object> eventData = new Dictionary<string, object>();
        protected List<IContext> contexts = new List<IContext>();
        protected string schema;

        public SelfDescribing GetSPEvent()
        {
            SelfDescribing ev = new SelfDescribing(schema, eventData);
            var evContexts = ev.GetContexts();
            foreach (RuzdContext c in contexts)
            {
                evContexts.Add(c.Build());
            }
            ev.SetCustomContext(evContexts);
            return ev;
        }

        public void addContext(RuzdContext context)
        {
            // check if context of this class already exists
            foreach (RuzdContext c in contexts)
            {
                if (c.GetType() == context.GetType())
                {
                    Log.Error("Context of this type was already added to the event.");
                    return;
                }
            }
            contexts.Add(context);
        }
    }

    public class GameEvent : RuzdEvent
    {
        public GameEvent(string action, string category = null, string label = null, string value = null)
        {
            schema = RuzdEventSchemas.gameEvent;
            eventData.Add("action", action);
            if (!string.IsNullOrEmpty(category)) eventData.Add("category", category);
            if (!string.IsNullOrEmpty(value)) eventData.Add("value", value);
            if (!string.IsNullOrEmpty(label)) eventData.Add("label", label);
        }
    }

    public class ProgressEvent : RuzdEvent
    {
        public ProgressEvent(string action, string category = null, string label = null, string value = null)
        {
            schema = RuzdEventSchemas.progressEvent;
            eventData.Add("action", action);
            if (!string.IsNullOrEmpty(category)) eventData.Add("category", category);
            if (!string.IsNullOrEmpty(value)) eventData.Add("value", value);
            if (!string.IsNullOrEmpty(label)) eventData.Add("label", label);
        }
    }

    public class ResourceEvent : RuzdEvent
    {
        public ResourceEvent(string action, string category = null, string label = null, string value = null)
        {
            schema = RuzdEventSchemas.resourceEvent;
            eventData.Add("action", action);
            if (!string.IsNullOrEmpty(category)) eventData.Add("category", category);
            if (!string.IsNullOrEmpty(value)) eventData.Add("value", value);
            if (!string.IsNullOrEmpty(label)) eventData.Add("label", label);
        }
    }

    public class CustomEvent : RuzdEvent
    {
        public CustomEvent(string action, string category = null, Dictionary<string, object> data = null)
        {
            schema = RuzdEventSchemas.resourceEvent;
            eventData.Add("action", action);
            if (!string.IsNullOrEmpty(category)) eventData.Add("category", category);
            if (data != null)
            {
                string jsonData = JsonConvert.SerializeObject(data);
                // check if json string is not too big
                if (jsonData.Length > RuzdEventSchemas.customEventMaxSize)
                {
                    Log.Error("Custom event data is too big (512 chars max).");
                }
                else
                {
                    eventData.Add("data", jsonData);
                }
            }
        }
    }

    public class ErrorEvent : RuzdEvent
    {
        public ErrorEvent(int severity, string message, Dictionary<string, object> data = null)
        {
            schema = RuzdEventSchemas.errorEvent;
            eventData.Add("severity", severity);
            eventData.Add("message", message);
            if (data != null)
            {
                string jsonData = JsonConvert.SerializeObject(data);
                // check if json string is not too big
                if (jsonData.Length > RuzdEventSchemas.customEventMaxSize)
                {
                    Log.Error("Error event data is too big (512 chars max).");
                }
                else
                {
                    eventData.Add("data", jsonData);
                }
            }
        }
    }
}
