Hangfire.Redis.StackExchange
==============

A quick port of the old Hangfire.Redis library to use StackExchange.Redis

## Documentation
Basic configuration mimics [stock Hangfire behaviour](http://docs.hangfire.io/en/latest/configuration/using-redis.html). However, it's possible to pass your StackExchange.Redis ConfigurationOptions in directly.

## Configuration
#### Basic Setup

There are many ways to configure Hangfire to use Hangfire.Redis.StackExchange. To get going in a hurry you can add something as simple as this to your OWIN Startup class:

```c#
using Hangfire;
using Hangfire.Redis.StackExchange;

// ...

public void Configuration(IAppBuilder app)
{
    GlobalConfiguration.Configuration.UseRedisStorage("<connection string>");

    app.UseHangfireDashboard();
    app.UseHangfireServer();
}
```

If you already have a StackExchange.Redis `ConfigurationOptions` class that your project uses you are able to substitute it for the connection string:

```c#
GlobalConfiguration.Configuration.UseRedisStorage(ConfigurationOptions);
```

Further options for configuration can be found in the `RedisStorageOptions` class:
```c#
GlobalConfiguration.Configuration.UseRedisStorage("localhost:6379", new RedisStorageOptions()
{
	Db = 0,
	Prefix = "hangfire:"
});
```

#### Dashboard
You can display extra stats from your Redis server on your dashboard. If you use an admin connection by default you can do so as easily as:
```c#
GlobalConfiguration.Configuration.UseDashboardMetric(GetDashboardInfo("Version", "redis_version"));
```
If you maintain a seperate `ConnectionMultiplexer` with administrator privilages you are able to use that instead:
```c#
GlobalConfiguration.Configuration.UseDashboardMetric(GetDashboardInfo(AdminMultiplexer, "Version", "redis_version"));
```
The above two examples select a server at random to pull INFO from. This is not an issue if you only have one Redis server, but if you have them clustered you can retrieve information from each seperately by passing an `IServer`:
```c#
GlobalConfiguration.Configuration.UseDashboardMetric(GetDashboardInfo(IServer, "Server 1: Version", "redis_version"));
```
A list of avaiable INFO keys can be found at [here](http://redis.io/commands/INFO).

## NuGet

Can be found on NuGet at https://www.nuget.org/packages/HangFire.Redis.SE/

## Changelog
### 1.4.0
* Fixed issues with multiple queues

### 1.2.0
* Added support for changing the hangfire prefix
* Fixed issue where job status was not always being reported to the dashboard

### 1.1.1
* Fix function name in [Obsolete] sections

### 1.1.0
* Updated to support for Hangfire 1.4
* Fixed job state always being null on the dashboard

### 1.0.0
* Initial Version