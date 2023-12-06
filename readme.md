# Ruzd Unity Analytics SDK


## Installation

## Requirements

- Tested with Unity 2020.1 or newer
- The package depends on [Newtonsoft.Json](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@2.0/manual/index.html). If you don't have it in your project already, you need to install it. 

### Git URL

In Unity Package Manager choose `Add package from git URL`.

In the text box enter the following:

```
https://github.com/ruzd-gametech/unity-analytics-sdk.git
```

### Unity Package Manager

#### Adding OpenUPM to your project

1. In Unity, open the Edit menu, and choose Project Settings.
2. In the list of sections at the left hand side of the window, select Package Manager.

You need to add the following entry to the list of registries:

1. In the Name field, type OpenUPM.
2. In the URL field, type https://package.openupm.com.
3. In the Scopes field, type com.ruzd.
4. Save.

#### Installing the package

1. Open the Window menu, and choose Package Manager.
2. In the toolbar, click Packages: In Project, and choose My Registries.
3. Select the Ruzd Analytics SDK package, and click Install.

## Usage

### Initialize

You need to call `RuzdAnalytics.Analytics.Configure` before you can use the SDK.
It should be called only a single time. If you are not sure if your code is the first to call it, you can check with `RuzdAnalytics.Analytics.IsInitialized()`.

```csharp
RuzdAnalytics.Analytics.SetLogLevel(1);
if (!RuzdAnalytics.Analytics.IsInitialized())
{
    RuzdAnalytics.Analytics.Configure("<TRACKING_ID>");
    RuzdAnalytics.Analytics.SetBuildVersion("0.3.1_beta");
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

To track a general event you can use `RuzdAnalytics.Analytics.TrackGameEvent`. Usually you also want to add the run context to the event. You can do this by using `RuzdAnalytics.Analytics.TrackRunEvent` which works like `TrackGameEvent` but takes the identifier of the run and the time in seconds off the run time as additional parameters.

```csharp
RuzdAnalytics.Analytics.TrackRunEvent(current_runid.ToString(), 0, "started");
// some time later
RuzdAnalytics.Analytics.TrackRunEvent(current_runid.ToString(), 12, "gravity_changed", value: gravity.ToString());
// some time later
RuzdAnalytics.Analytics.TrackRunEvent(current_runid.ToString(), 35, "finished", value: "reason: death");
```
