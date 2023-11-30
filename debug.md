# Debugging Ruzd Analytics

## Local Data

The Ruzd Analytics SDK is compatible with snowplow event tracking (using the same event schema) and can be debugged with the [snowplow micro](https://docs.snowplow.io/docs/testing-debugging/snowplow-micro/what-is-micro/) tool.

To start snowplow micro you need to have docker installed and run the following command:

```bash
docker run -p 9090:9090 -e MICRO_IGLU_REGISTRY_URL="http://ruzd-schemas.s3-website.eu-central-1.amazonaws.com/" snowplow/snowplow-micro:2.0.0
```

This will start snowplow micro on port 9090. You can open the ui in your browser by navigating to http://localhost:9090/micro/ui/.

To send events to snowplow micro you need to setup the tracking endpoint like this:

```csharp
RuzdAnalytics.Analytics.Setup("localhost:9090", "my_tracking_id",
buildVersion: "beta_0.1", httpProtocol: HttpProtocol.HTTP,
customPath: "/com.snowplowanalytics.snowplow/tp2");
```
