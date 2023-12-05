using SnowplowTracker;
using SnowplowTracker.Enums;
using SnowplowTracker.Payloads.Contexts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SocialPlatforms;

namespace RuzdAnalytics
{
    public class GameSession : ISession
    {
        private string SESSION_DEFAULT_PATH = "ruzd_session.dict";

        private Analytics ruzdAnalyticsInstance;
        private SessionContext sessionContext;

        private bool enablePing;
        private long pingInterval;
        private long backgroundSessionTimeout;
        private long checkInterval = 15;
        private bool background = false;
        private long lastEventTimestamp;

        private string firstEventId;
        private string currentSessionId;
        private string previousSessionId;
        private int sessionIndex;

        private Timer sessionCheckTimer;
        private string SessionPath;
        private readonly StorageMechanism sessionStorage = StorageMechanism.LocalStorage;


        public GameSession(Analytics analyticsInstance, long backgroundSessionTimeout = 1800, bool enablePing = true, long pingInterval = 120, string customSessionPath = null)
        {
            this.ruzdAnalyticsInstance = analyticsInstance;
            this.enablePing = enablePing;
            this.pingInterval = pingInterval ;
            this.backgroundSessionTimeout = backgroundSessionTimeout;
            if (string.IsNullOrEmpty(customSessionPath))
            {
                this.SessionPath = SESSION_DEFAULT_PATH;
            }
            else
            {
                this.SessionPath = customSessionPath;
            }

            // Try to get the previous session from storage
            Dictionary<string, object> maybeSessionDict = ReadSessionDictionary();
            if (maybeSessionDict == null)
            {
                currentSessionId = null;
            }
            else
            {
                if (maybeSessionDict.TryGetValue(Constants.SESSION_ID, out var sessionId))
                {
                    this.currentSessionId = (string)sessionId;
                }
                if (maybeSessionDict.TryGetValue(Constants.SESSION_PREVIOUS_ID, out var previousId))
                {
                    this.previousSessionId = (string)previousId;
                }
                if (maybeSessionDict.TryGetValue(Constants.SESSION_INDEX, out var sessionIndex))
                {
                    this.sessionIndex = Convert.ToInt32(sessionIndex);
                }
            }

            NewSession();
        }

        private void UpdateSession()
        {
            previousSessionId = currentSessionId;
            currentSessionId = Utils.GetGUID();
            sessionIndex++;
            firstEventId = null;
        }

        private void UpdateSessionDict()
        {
            string userId = ruzdAnalyticsInstance.GetPlayerId();
            SessionContext newSessionContext = new SessionContext()
                    .SetUserId(userId)
                    .SetSessionId(currentSessionId)
                    .SetPreviousSessionId(previousSessionId)
                    .SetSessionIndex(sessionIndex)
                    .SetStorageMechanism(sessionStorage)
                    .Build();
            sessionContext = newSessionContext;
        }

        private void UpdateLastEventTimestamp()
        {
            lastEventTimestamp = Utils.GetTimestamp();
        }

        public SessionContext GetSessionContext(string eventId)
        {
            UpdateLastEventTimestamp();
            if (firstEventId == null)
            {
                firstEventId = eventId;
                sessionContext.SetFirstEventId(eventId);
                sessionContext.Build();
            }
            Log.Verbose("Session: data: " + Utils.DictToJSONString(sessionContext.GetData()));
            return sessionContext;
        }

        public string GetSessionId()
        {
            return currentSessionId;
        }

        public void NewSession()
        {
            UpdateSession();
            UpdateLastEventTimestamp();
            UpdateSessionDict();
            WriteSessionDictionary(sessionContext.GetData());
        }

        public void SetBackground(bool truth)
        {
            this.background = truth;
        }

        public void StartChecker()
        {
            if (sessionCheckTimer == null)
            {
                sessionCheckTimer = new Timer(CheckSession, null, checkInterval * 1000, Timeout.Infinite);
            }
        }

        public void StopChecker()
        {
            if (sessionCheckTimer != null)
            {
                sessionCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);
                sessionCheckTimer.Dispose();
                sessionCheckTimer = null;
            }
        }

        private void CheckSession(object state)
        {
            Log.Verbose("Session: Checking session...");
            long checkTime = Utils.GetTimestamp();
            // if game is in background, check if session has expired
            if (background)
            {
                if (!Utils.IsTimeInRange(lastEventTimestamp, checkTime, backgroundSessionTimeout*1000))
                {
                    Log.Debug("Session: Session expired in background, starting new session");
                    NewSession();
                }
            }
            // if game is in foreground, check if ping is needed
            else
            {
                if (enablePing && !Utils.IsTimeInRange(lastEventTimestamp, checkTime, pingInterval*1000))
                {
                    Log.Debug("Session: No event in last " + pingInterval + " seconds, sending ping");
                    UpdateLastEventTimestamp();
                    ruzdAnalyticsInstance.SendPingEvent();
                }
            }
            sessionCheckTimer.Change(checkInterval * 1000, Timeout.Infinite);
        }

        private Dictionary<string, object> ReadSessionDictionary()
        {
            if (IsUsingPlayerProfsSessionStorage())
            {
                return Utils.ReadDictionaryFromPlayerPrefs(GetSessionPath());
            }
            else
            {
                return Utils.ReadDictionaryFromFile(GetSessionPath());
            }
        }

        private bool WriteSessionDictionary(Dictionary<string, object> dictionary)
        {
            if (IsUsingPlayerProfsSessionStorage())
            {
                return Utils.WriteDictionaryToPlayerPrefs(GetSessionPath(), dictionary);
            }
            else
            {
                return Utils.WriteDictionaryToFile(GetSessionPath(), dictionary);
            }
        }

        private string GetSessionPath()
        {
            if (IsUsingPlayerProfsSessionStorage())
            {
                return SessionPath ?? SESSION_DEFAULT_PATH;
            }
            else
            {
                return $"{Application.persistentDataPath}/{SessionPath ?? SESSION_DEFAULT_PATH}";
            }
        }

        private bool IsUsingPlayerProfsSessionStorage()
        {
            return Application.platform == RuntimePlatform.tvOS || Application.platform == RuntimePlatform.WebGLPlayer;
        }

        public void SetPingEnabled(bool enabled)
        {
            enablePing = enabled;
        }
    }
}
