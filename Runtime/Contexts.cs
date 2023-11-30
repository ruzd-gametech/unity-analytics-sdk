using Newtonsoft.Json;
using SnowplowTracker;
using SnowplowTracker.Payloads;
using SnowplowTracker.Payloads.Contexts;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RuzdAnalytics
{
    public class RuzdContextSchemas
    {
        public readonly static string runContext = "iglu:com.ruzd/runContext/jsonschema/1-0-0";
        public readonly static string systemContext = "iglu:com.ruzd/systemContext/jsonschema/1-0-0";
        public readonly static string locationContext = "iglu:com.ruzd/locationContext/jsonschema/1-0-0";
        public readonly static string fpsContext = "iglu:com.ruzd/fpsContext/jsonschema/1-0-0";
        public readonly static string customContext = "iglu:com.ruzd/customContext/jsonschema/1-0-0";
    }

    public class RuzdContext : AbstractContext<RuzdContext>
    {
        public override RuzdContext Build()
        {
            context = new SelfDescribingJson(this.schema, this.data);
            return this;
        }
    }

    public class LocationContext : RuzdContext
    {
        public LocationContext(string mapName, double locationX, double locationY)
        {
            schema = RuzdContextSchemas.locationContext;
            DoAdd("mapName", mapName);
            DoAdd("locationX", locationX);
            DoAdd("locationY", locationY);
        }

        public LocationContext SetMapName(string mapName)
        {
            this.DoAdd("mapName", mapName);
            return this;
        }

        public LocationContext SetLocationX(double locationX)
        {
            this.DoAdd("locationX", locationX);
            return this;
        }

        public LocationContext SetLocationY(double locationY)
        {
            this.DoAdd("locationY", locationY);
            return this;
        }
    }

    public class RunContext : RuzdContext
    {
        public RunContext(string runId, long playTimeSeconds)
        {
            schema = RuzdContextSchemas.runContext;
            DoAdd("runId", runId);
            DoAdd("playTimeSeconds", playTimeSeconds);
        }

        public RunContext SetRunId(string runId)
        {
            this.DoAdd("runId", runId);
            return this;
        }

        public RunContext SetPlayTimeSeconds(long playTimeSeconds)
        {
            this.DoAdd("playTimeSeconds", playTimeSeconds);
            return this;
        }
    }

    public class SystemContext : RuzdContext
    {
        public SystemContext(string deviceIdentifier = null)
        {
            schema = RuzdContextSchemas.systemContext;

            if (!string.IsNullOrEmpty(deviceIdentifier)) DoAdd("dId", deviceIdentifier);

            DoAdd("dModel", SystemInfo.deviceModel);
            DoAdd("dType", SystemInfo.deviceType.ToString());
            DoAdd("gName", SystemInfo.graphicsDeviceName);
            DoAdd("gType", SystemInfo.graphicsDeviceType.ToString());
            DoAdd("gMem", SystemInfo.graphicsMemorySize);
            DoAdd("osName", SystemInfo.operatingSystem);
            DoAdd("pType", SystemInfo.processorType);
            DoAdd("pCount", SystemInfo.processorCount);
            DoAdd("pFreq", SystemInfo.processorFrequency);
            DoAdd("sysMem", SystemInfo.systemMemorySize);
        }

        public SystemContext SetDeviceIdentifier(string deviceIdentifier)
        {
            this.DoAdd("dId", deviceIdentifier);
            return this;
        }

        public SystemContext SetDeviceModel(string deviceModel)
        {
            this.DoAdd("dModel", deviceModel);
            return this;
        }

        public SystemContext SetDeviceType(string deviceType)
        {
            this.DoAdd("dType", deviceType);
            return this;
        }

        public SystemContext SetGraphicsDeviceName(string graphicsDeviceName)
        {
            this.DoAdd("gName", graphicsDeviceName);
            return this;
        }

        public SystemContext SetGraphicsDeviceType(string graphicsDeviceType)
        {
            this.DoAdd("gType", graphicsDeviceType);
            return this;
        }

        public SystemContext SetGraphicsMemorySize(int graphicsMemorySize)
        {
            this.DoAdd("gMem", graphicsMemorySize);
            return this;
        }

        public SystemContext SetOsName(string osName)
        {
            this.DoAdd("osName", osName);
            return this;
        }

        public SystemContext SetProcessorType(string processorType)
        {
            this.DoAdd("pType", processorType);
            return this;
        }

        public SystemContext SetProcessorCount(int processorCount)
        {
            this.DoAdd("pCount", processorCount);
            return this;
        }

        public SystemContext SetProcessorFrequency(int processorFrequency)
        {
            this.DoAdd("pFreq", processorFrequency);
            return this;
        }

        public SystemContext SetSystemMemorySize(int systemMemorySize)
        {
            this.DoAdd("sysMem", systemMemorySize);
            return this;
        }
    }

    public class FpsContext : RuzdContext
    {
        public FpsContext(double averageFPS, Nullable<double> minFPS95 = null, Nullable<double> minFPS99 = null)
        {
            schema = RuzdContextSchemas.fpsContext;
            DoAdd("averageFPS", averageFPS);
            if (minFPS95.HasValue) DoAdd("minFPS95", minFPS95.Value);
            if (minFPS99.HasValue) DoAdd("minFPS99", minFPS99.Value);
        }

        public FpsContext SetAverageFPS(double averageFPS)
        {
            this.DoAdd("averageFPS", averageFPS);
            return this;
        }

        public FpsContext SetMinFPS95(double minFPS95)
        {
            this.DoAdd("minFPS95", minFPS95);
            return this;
        }

        public FpsContext SetMinFPS99(double minFPS99)
        {
            this.DoAdd("minFPS99", minFPS99);
            return this;
        }
    }

    public class CustomContext : RuzdContext
    {
        public CustomContext(string name, Dictionary<string, object> data)
        {
            schema = RuzdContextSchemas.customContext;
            DoAdd("name", name);
            string jsonData = JsonConvert.SerializeObject(data);
            // check if json string is not too big
            if (jsonData.Length > RuzdEventSchemas.customEventMaxSize)
            {
                Log.Error("Custom context data is too big (512 chars max).");
            }
            else
            {
                DoAdd("data", jsonData);
            }
        }
    }
}
