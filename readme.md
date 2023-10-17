# Ruzd Unity Analytics SDK


## Installation

### Git URL

In Unity Package Manager choose `Add package from git URL`.

In the text box enter the following:

```
https://github.com/ruzd-gametech/unity-analytics-sdk.git
```

### Unity Package Manager

TBA

## Usage

### Initialize

You need to call `RuzdAnalytics.Analytics.Setup` before you can use the SDK.
It should be called only a single time. If you are not sure if your code is the first to call it, you can check with `RuzdAnalytics.Analytics.IsInitialized()`.

```csharp
if (!RuzdAnalytics.Analytics.IsInitialized())
{
    RuzdAnalytics.Analytics.SetLogLevel(1);
    RuzdAnalytics.Analytics.Setup("<TRACKING_ENDPOINT>", "<TRACKING_ID>");
}
```

### Set User ID

You can set the user ID with `RuzdAnalytics.Analytics.SetUserId`.
This user ID should be unique for each user.

If you don't set the user ID, the SDK will generate one for you. The ID Ruzd is generating is a random ID that is not directly identifying the user and because of this, is not unique across devices and might change if the user reinstalls the app.

```csharp
RuzdAnalytics.Analytics.SetUserId("<USER_ID>");
```

### Start Event Tracking

To start tracking events you need to call `RuzdAnalytics.Analytics.StartTracking`.
If you have to get the user consent for tracking make sure that you call `RuzdAnalytics.Analytics.StartTracking` after the user has given consent.

```csharp
RuzdAnalytics.Analytics.StartTracking();
```


### Track Event

To track a general event you can use `RuzdAnalytics.Analytics.TrackGameEvent`. Usually you also want to add the run context to the event. You can do this by updating the run context with `RuzdAnalytics.Analytics.UpdateRun(<ID_OF_THE_RUN>, <RUN_PLAY_TIME_IN_SECONDS>)` and using `RuzdAnalytics.Analytics.TrackRunEvent` afterwards.

```csharp
RuzdAnalytics.Analytics.UpdateRun(current_runid.ToString(), 0);
RuzdAnalytics.Analytics.TrackRunEvent("started");
// some time later
RuzdAnalytics.Analytics.UpdateRun(current_runid.ToString(), 12);
RuzdAnalytics.Analytics.TrackRunEvent("gravity_changed", value: gravity.ToString());
// some time later
RuzdAnalytics.Analytics.UpdateRun(current_runid.ToString(), 35);
RuzdAnalytics.Analytics.TrackRunEvent("finished", value: "reason: death");
```