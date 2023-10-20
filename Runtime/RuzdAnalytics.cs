using System.Collections.Generic;
using UnityEngine;
using SnowplowTracker;
using SnowplowTracker.Emitters;
using SnowplowTracker.Events;
using SnowplowTracker.Enums;
using SnowplowTracker.Payloads.Contexts;
using SnowplowTracker.Payloads;
using SnowplowTracker.Storage;

namespace RuzdAnalytics
{
    public class Analytics : MonoBehaviour
    {
        private static Analytics _instance;
        private IEmitter emitter;
        private IStore store;
        private Tracker tracker;
        private Subject subject;
        private Session session;
        private GameRun currentRun;
        private SystemContext sysContext;
        private bool trackingRunning = false;
        private bool trackingSetup = false;
        private long lastFPSEvent;
        private int FPS_MIN_INTERVAL_SECONDS = 60;
        private readonly string POST_SUFFIX = "/com.ruzd/tp2";
        private readonly string GET_SUFFIX = "/i";
        private string defaultPlayerId;

        public HttpMethod httpMethod = HttpMethod.POST;
        public HttpProtocol httpProtocol = HttpProtocol.HTTPS;
        public string trackingPath;
        public string trackingEndpoint;
        public string ruzdGameId;
        public string customPlayerId;
        public string customVersion;

        private static readonly object Lock = new object();
        public static bool Quitting { get; private set; }

        // Singleton Pattern Instance
        public static Analytics Instance
        {
            get
            {
                if (Quitting)
                {
                    Log.Warning("[RuzdAnalytics] Not returning Analytics instance because the application is quitting.");
                    return null;
                }
                lock (Lock)
                {
                    if (_instance != null)
                    {
                        return _instance;
                    }

                    // Searching for GameObjects
                    var instances = FindObjectsOfType<Analytics>();
                    var count = instances.Length;
                    if (count > 0)
                    {
                        if (count == 1)
                            return _instance = instances[0];
                        Log.Warning($"[RuzdAnalytics] There should never be more than one Analytics in the scene, but {count} were found. The first instance found will be used, and all others will be destroyed.");
                        for (var i = 1; i < instances.Length; i++)
                            Destroy(instances[i]);
                        return _instance = instances[0];
                    }

                    Log.Debug($"[RuzdAnalytics] An instance is needed in the scene and no existing instances were found, so a new instance will be created. Avoid this by creating a GameObject manualy.");
                    return _instance = new GameObject("Ruzd Analytics").AddComponent<Analytics>();
                }
            }
        }

        // Methods called by Unity
        void OnApplicationQuit()
        {
            Quitting = true;
        }

        private void OnDestroy()
        {
            Quitting = true;
            OnTrackingStop();
        }

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            OnAwake();
        }

        // Tracker Setup
        // OnAwake is called when the instance is created
        void OnAwake()
        {
            Log.SetLogLevel(1);

            // Setup Event Store
            if (Application.platform == RuntimePlatform.tvOS)
            {
                store = new InMemoryEventStore();
            }
            else
            {
                BoundedEventStore bStore = new BoundedEventStore(maxSize: 500);
                // Delete old Events
                bStore.DeleteOldEvents(ttlSeconds: 60*60*24);
                store = bStore;
            }

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                emitter = new WebGlEmitter("localhost", HttpProtocol.HTTP, httpMethod, eventStore: store);
            }
            else
            {
                emitter = new AsyncEmitter("localhost", HttpProtocol.HTTP, httpMethod, eventStore: store);
            }

            // Get or generate a unique user identifier
            defaultPlayerId = PlayerPrefs.GetString("ruzd_player_id", System.Guid.NewGuid().ToString());
            PlayerPrefs.SetString("ruzd_player_id", defaultPlayerId);
            PlayerPrefs.Save();

            // Preset FPS timer
            lastFPSEvent = Utils.GetTimestamp();
            sysContext = SystemContext.GetSystemContext();

            Log.Debug("[RuzdAnalytics] Inititalized");
        }

        // OnConfigurationChanged is called after the user setup
        public void OnConfigurationChanged()
        {
            // Log warning if this is called again after the setup
            if (trackingSetup)
            {
                Log.Warning("[RuzdAnalytics] Configuration changed after initial setup. Please make sure Analytics.Setup is only called once.");
            }

            // Setup Emitter
            emitter.SetHttpMethod(httpMethod);
            emitter.SetHttpProtocol(httpProtocol);

            if(!string.IsNullOrEmpty(trackingPath))
            {
                emitter.SetCollectorUri($"{trackingEndpoint}{trackingPath}");
            }
            else
            {
                if (httpMethod == HttpMethod.GET)
                {
                    emitter.SetCollectorUri($"{trackingEndpoint}{GET_SUFFIX}");
                }
                else if (httpMethod == HttpMethod.POST)
                {
                    emitter.SetCollectorUri($"{trackingEndpoint}{POST_SUFFIX}");
                }
            }
            subject = new Subject();
            var screenRes = AnalyticsUtils.GetScreenResolution();
            var windowRes = AnalyticsUtils.GetGameResolution();
            subject.SetScreenResolution(screenRes.width, screenRes.height);
            subject.SetViewPort(windowRes.width, windowRes.height);
            subject.SetUserId(GetPlayerId());
            // subject.SetTimezone(AnalyticsUtils.GetTimezone());
            session = new Session("ruzd_game_session", foregroundTimeout: 1800, backgroundTimeout: 1800, checkInterval: 30, customUserId: GetPlayerId());
            tracker = new Tracker(emitter, GetBuildVersion(), ruzdGameId, subject: subject, session: session, platform: AnalyticsUtils.GetPlatform());         
            trackingSetup = true;
        }

        public void OnTrackingStart()
        {
            if (!trackingSetup)
            {
                Log.Warning("[RuzdAnalytics] Run Analytics.Setup() with valid configuration before starting event tracking.");
                return;
            }
            tracker.StartEventTracking();
            trackingRunning = true;
            Log.Debug("[RuzdAnalytics] Tracking started");
        }

        public void OnTrackingStop()
        {
            if (tracker != null)
                tracker.StopEventTracking();
            trackingSetup = false;
            trackingRunning = false;
            Log.Debug("[RuzdAnalytics] Tracking stopped");
        }

        string GetPlayerId()
        {
            if (!string.IsNullOrEmpty(customPlayerId))
            {
                return customPlayerId;
            }
            return defaultPlayerId;
        }

        string GetBuildVersion()
        {
            if (!string.IsNullOrEmpty(customVersion))
                return customVersion;
            return Application.version;
        }

        public SystemContext GetSystemContext()
        {
            return sysContext;
        }

        public void _TrackEvent(IEvent newEvent, bool withRunContext=true)
        {
            if (!trackingSetup)
            {
                Log.Warning("[RuzdAnalytics] Run Analytics.Setup() with valid configuration before tracking Events.");
                return;
            }
            if (!trackingRunning)
            {
                Log.Warning("[RuzdAnalytics] Event will not be recorded because event tracking was not started.");
                return;
            }

            System.Type eType = newEvent.GetType();
            
            if (withRunContext && currentRun != null)
            {
                if (eType == typeof(SelfDescribing))
                {
                    SelfDescribing newNewEvent = (SelfDescribing)newEvent;
                    var cc = newNewEvent.GetContexts();
                    cc.Add(currentRun.GetRunContext());
                    newNewEvent.SetCustomContext(cc);
                    tracker.Track(newNewEvent);
                    return;
                }
                if (eType == typeof(Structured))
                {
                    Structured newNewEvent = (Structured)newEvent;
                    var cc = newNewEvent.GetContexts();
                    cc.Add(currentRun.GetRunContext());
                    newNewEvent.SetCustomContext(cc);
                    tracker.Track(newNewEvent);
                    return;
                }
            }

            tracker.Track(newEvent);
        }

        public void setBuildVersion(string buildVersion)
        {
            customVersion = buildVersion;
        }

        public void SetRun(string runIdentifier, long playTimeSeconds)
        {
            if (currentRun == null)
            {
                currentRun = new GameRun(runIdentifier, playTimeSeconds);
                return;
            }
            currentRun.update(runIdentifier, playTimeSeconds);
        }

        public void TrackFPSEvent(int averageFPS)
        {
            long checkTime = Utils.GetTimestamp();
            if (Utils.IsTimeInRange(lastFPSEvent, checkTime, FPS_MIN_INTERVAL_SECONDS*1000))
            {
                return;
            }
            Dictionary<string, object> event_data = new Dictionary<string, object>
            {
                { "averageFPS", averageFPS }
            };
            SelfDescribing e = new SelfDescribing("iglu:com.ruzd/fps/jsonschema/1-0-0", event_data);
            lastFPSEvent = checkTime;
            _TrackEvent(e.Build());
        }


        // Public static methods called usually by the user
        public static void TrackResourceEvent(string resourceName, double amount, string category = null, string label = null)
        {
            Dictionary<string, object> event_data = new Dictionary<string, object>
            {
                { "resourceName", resourceName },
                { "amount", amount }
            };
            if (!string.IsNullOrEmpty(category)) event_data.Add("category", category);
            if (!string.IsNullOrEmpty(label)) event_data.Add("label", label);
            SelfDescribing e = new SelfDescribing("iglu:com.ruzd/resourceEvent/jsonschema/1-0-0", event_data);
            Instance._TrackEvent(e.Build(), withRunContext: true);
        }

        public static void TrackRunEvent(string action, string category = null, string label = null, string value = null)
        {
            Dictionary<string, object> event_data = new Dictionary<string, object>
            {
                { "action", action }
            };
            if (!string.IsNullOrEmpty(category)) event_data.Add("category", category);
            if (!string.IsNullOrEmpty(value)) event_data.Add("value", value);
            if (!string.IsNullOrEmpty(label)) event_data.Add("label", label);
            SelfDescribing e = new SelfDescribing("iglu:com.ruzd/runEvent/jsonschema/1-0-0", event_data);
            Instance._TrackEvent(e.Build(), withRunContext: true);
        }

        public static void TrackGameEvent(string action, string category = null, string label = null, string value = null)
        {
            Dictionary<string, object> event_data = new Dictionary<string, object>
            {
                { "action", action }
            };
            if (!string.IsNullOrEmpty(category)) event_data.Add("category", category);
            if (!string.IsNullOrEmpty(value)) event_data.Add("value", value);
            if (!string.IsNullOrEmpty(label)) event_data.Add("label", label);
            SelfDescribing e = new SelfDescribing("iglu:com.ruzd/gameEvent/jsonschema/1-0-0", event_data);
            Instance._TrackEvent(e.Build(), withRunContext: false);
        }

        public static void TrackGameStart()
        {
            Dictionary<string, object> event_data = new Dictionary<string, object>
            {
                { "category", "game" },
                { "action", "start" },
                { "label", "systemContext" }
            };
            SelfDescribing e = new SelfDescribing("iglu:com.ruzd/gameEvent/jsonschema/1-0-0", event_data);
            var cc = e.GetContexts();
            cc.Add(Instance.GetSystemContext());
            e.SetCustomContext(cc);
            Instance._TrackEvent(e.Build(), withRunContext: false);
        }

        public static void TrackFPS(int averageFPS)
        {
            Instance.TrackFPSEvent(averageFPS);
        }

        public static void Setup(string trackingEndpoint, string ruzdGameId, string buildVersion = null,
                                 HttpMethod httpMethod = HttpMethod.POST, HttpProtocol httpProtocol = HttpProtocol.HTTPS,
                                 string customPath = null)
        {
            // Check Game Id
            if (ruzdGameId.Length < 8 || ruzdGameId.Length > 32)
            {
                Log.Error("[RuzdAnalytics] Setup failed, Invalid length of ruzdGameId");
                return;
            }
            // Check Tracking Endpoint (should not end with /)
            if (trackingEndpoint.EndsWith("/"))
            {
                trackingEndpoint = trackingEndpoint.Substring(0, trackingEndpoint.Length - 1);
            }
            if (!string.IsNullOrEmpty(buildVersion))
            {
                Instance.setBuildVersion(buildVersion);
            }
            Instance.trackingEndpoint = trackingEndpoint;
            Instance.ruzdGameId = ruzdGameId;
            Instance.httpMethod = httpMethod;
            Instance.httpProtocol = httpProtocol;
            if (!string.IsNullOrEmpty(customPath))
            {
                Instance.trackingPath = customPath;
            }
            Instance.OnConfigurationChanged();
        }

        public static void UpdateRun(string runIdentifier, long playTimeSeconds)
        {
            Instance.SetRun(runIdentifier, playTimeSeconds);
        }

        public static void StartTracking(bool dontSendStartEvent = false)
        {
            Instance.OnTrackingStart();
            if (!dontSendStartEvent)
            {
                TrackGameStart();
            }
        }

        public static void StopTracking()
        {
            Instance.OnTrackingStop();
        }

        public static void SetLogLevel(int logLevel)
        {
            Log.SetLogLevel(logLevel);
        }

        public static bool IsInitialized()
        {
            return Instance.trackingSetup;
        }

        public static bool IsRunning()
        {
            return Instance.trackingRunning;
        }
    }

    public class AnalyticsUtils
    {
        public static string GetTimezone()
        {
            System.TimeZoneInfo infos = System.TimeZoneInfo.Local;
            return infos.Id;
        }

        public static Resolution GetGameResolution()
        {
            Resolution res = new Resolution();
            res.height = Screen.height;
            res.width = Screen.width;
            return res;
        }

        public static Resolution GetScreenResolution()
        {
            return Screen.currentResolution;
        }

        public static string GetTrackerName()
        {
            string unityVersion = Application.unityVersion;
            if (Application.isEditor)
            {
                return $"unity-editor-{unityVersion}";
            }
            return $"unity-{unityVersion}";
        }

        public static DevicePlatforms GetPlatform()
        {
            var platform = Application.platform;
            if (platform == RuntimePlatform.WebGLPlayer)
            {
                return DevicePlatforms.Web;
            }
            else if (platform == RuntimePlatform.IPhonePlayer || platform == RuntimePlatform.Android)
            {
                return DevicePlatforms.Mobile;
            }
            else if (platform == RuntimePlatform.PS4 || platform == RuntimePlatform.PS5 || platform == RuntimePlatform.XboxOne || platform == RuntimePlatform.Switch)
            {
                return DevicePlatforms.GameConsole;
            }
            return DevicePlatforms.Desktop;
        }

        public static string generateId()
        {
            return Utils.GetGUID();
        }
    }

    public class RunContext : AbstractContext<RunContext>
    {
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

        public override RunContext Build()
        {
            Utils.CheckArgument(this.data.ContainsKey("runId"), "RunContext requires 'runId'.");
            this.schema = "iglu:com.ruzd/runContext/jsonschema/1-0-0";
            this.context = new SelfDescribingJson(this.schema, this.data);
            return this;
        }
    }

    public class GameRun
    {
        public string runId;
        public long playTimeSeconds;

        public GameRun(string runId, long playTimeSeconds)
        {
            this.runId = runId;
            this.playTimeSeconds = playTimeSeconds;
        }

        public void update(string runId, long playTimeSeconds)
        {
            if (runId == this.runId && playTimeSeconds < this.playTimeSeconds)
            {
                Log.Warning("[RuzdAnalytics] The current run was updated but the playTime decreased.");
            }
            this.runId = runId;
            this.playTimeSeconds = playTimeSeconds;
        }

        public RunContext GetRunContext()
        {
            RunContext c = new RunContext();
            c.SetRunId(runId);
            c.SetPlayTimeSeconds(playTimeSeconds);
            return c.Build();
        }
    }

    public class SystemContext : AbstractContext<SystemContext>
    {
        private static bool USE_DEVICE_ID = false;

        public override SystemContext Build()
        {
            this.schema = "iglu:com.ruzd/systemContext/jsonschema/1-0-0";
            this.context = new SelfDescribingJson(this.schema, this.data);
            return this;
        }
        public static SystemContext GetSystemContext()
        {
            SystemContext c = new SystemContext();
            if (USE_DEVICE_ID)
            {
                c.DoAdd("dId", SystemInfo.deviceUniqueIdentifier);
            }
            c.DoAdd("dModel", SystemInfo.deviceModel);
            c.DoAdd("dType", SystemInfo.deviceType.ToString());
            c.DoAdd("gName", SystemInfo.graphicsDeviceName);
            c.DoAdd("gType", SystemInfo.graphicsDeviceType.ToString());
            c.DoAdd("gMem", SystemInfo.graphicsMemorySize);
            c.DoAdd("osName", SystemInfo.operatingSystem);
            c.DoAdd("pType", SystemInfo.processorType);
            c.DoAdd("pCount", SystemInfo.processorCount);
            c.DoAdd("pFreq", SystemInfo.processorFrequency);
            c.DoAdd("sysMem", SystemInfo.systemMemorySize);
            return c.Build();
        }
    }

    public class FPSTracker
    {
        int frameCounter = 0;
        float timeCounter = 0.0f;
        float lastFramerate = 0.0f;
        float refreshTime;
        public int sendIntervalSeconds;
        bool trackingEnabled;
        long lastEventTime;

        public FPSTracker(float refreshTime, int sendIntervalSeconds = 180, bool sendEvents = true)
        {
            // Check if refreshTime is between 0.1 and 10 seconds
            if (refreshTime < 0.1f)
            {
                this.refreshTime = 0.1f;
            }
            else if (refreshTime > 10.0f)
            {
                this.refreshTime = 10.0f;
            }
            else
            {
                this.refreshTime = refreshTime;
            }
            this.sendIntervalSeconds = sendIntervalSeconds;
            this.trackingEnabled = sendEvents;
            this.lastEventTime = Utils.GetTimestamp();
        }

        public void tick(int frameCount, float timePassed)
        {
            if (timeCounter < refreshTime)
            {
                timeCounter += timePassed;
                frameCounter += frameCount;
            }
            else if (timeCounter > 0.0f && frameCounter > 0)
            {
                lastFramerate = (float)frameCounter / timeCounter;
                frameCounter = 0;
                timeCounter = 0.0f;
                Debug.Log($"Framerate: {lastFramerate}");
            }

            if (!trackingEnabled)
            {
                return;
            }

            var currentTime = Utils.GetTimestamp();
            if (!Utils.IsTimeInRange(lastEventTime, currentTime, sendIntervalSeconds*1000) && lastFramerate > 0.0f)
            {
                Log.Debug($"Framerate Event: {lastFramerate}");
                Analytics.Instance.TrackFPSEvent((int)lastFramerate);
            }
        }

        public void OnFrame()
        {
            tick(1, Time.deltaTime);
        }

        public float GetFPS()
        {
            return lastFramerate;
        }
    }
}