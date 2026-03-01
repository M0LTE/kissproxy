using kissproxy;
using kissproxylib;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace kissproxy;

/// <summary>
/// Web API endpoints for kissproxy management.
/// </summary>
public static class WebApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    /// <summary>
    /// Maps all API endpoints to the web application.
    /// </summary>
    public static void MapEndpoints(
        IEndpointRouteBuilder app,
        ConfigManager configManager,
        ModemStateManager stateManager,
        Dictionary<string, KissProxy> proxyInstances)
    {
        // Login endpoint (no auth required)
        app.MapPost("/api/login", (HttpContext ctx) => HandleLogin(ctx, configManager));

        // All other API endpoints require authentication
        var api = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            if (!IsAuthenticated(httpContext, configManager))
            {
                return Results.Unauthorized();
            }
            return await next(context);
        });

        // Config status
        api.MapGet("/config-status", () => new
        {
            writeable = configManager.IsWriteable,
            path = configManager.ConfigPath,
            needsMigration = configManager.NeedsMigration
        });

        // Modems - list all
        api.MapGet("/modems", () =>
        {
            var modems = configManager.Config.Modems.Select(m => new
            {
                config = m,
                state = stateManager.GetSnapshot(m.Id)
            });
            return Results.Json(modems, JsonOptions);
        });

        // Modems - get single
        api.MapGet("/modems/{id}", (string id) =>
        {
            var config = configManager.GetModem(id);
            if (config == null)
                return Results.NotFound();

            return Results.Json(new
            {
                config,
                state = stateManager.GetSnapshot(id)
            }, JsonOptions);
        });

        // Modems - get stats only (lightweight polling endpoint)
        // Returns empty state for new modems that don't have a proxy instance yet
        api.MapGet("/modems/{id}/stats", (string id) =>
        {
            // Verify the modem exists in config
            var config = configManager.GetModem(id);
            if (config == null)
                return Results.NotFound();

            // Return state if available, otherwise return empty state
            var state = stateManager.GetSnapshot(id);
            return Results.Json(state ?? new ModemState { Id = id }, JsonOptions);
        });

        // Modems - update
        api.MapPut("/modems/{id}", async (string id, HttpContext ctx) =>
        {
            try
            {
                var config = await ctx.Request.ReadFromJsonAsync<Config>(JsonOptions);
                if (config == null)
                    return Results.BadRequest("Invalid config");

                if (config.Id != id)
                    return Results.BadRequest("ID mismatch");

                configManager.UpdateModem(config);

                // Notify the proxy instance of config change
                if (proxyInstances.TryGetValue(id, out var proxy))
                {
                    proxy.OnConfigChanged(config);
                }

                return Results.Ok();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // Modems - add new
        api.MapPost("/modems", async (HttpContext ctx) =>
        {
            try
            {
                var config = await ctx.Request.ReadFromJsonAsync<Config>(JsonOptions);
                if (config == null)
                    return Results.BadRequest("Invalid config");

                configManager.AddModem(config);
                return Results.Created($"/api/modems/{config.Id}", config);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // Modems - delete
        api.MapDelete("/modems/{id}", (string id) =>
        {
            try
            {
                configManager.RemoveModem(id);
                stateManager.Remove(id);
                return Results.Ok();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // Modems - apply config NOW (send params to modem)
        // Returns 202 Accepted if proxy not running yet (params will be sent when it starts)
        api.MapPost("/modems/{id}/apply", (string id) =>
        {
            var config = configManager.GetModem(id);
            if (config == null)
                return Results.NotFound();

            if (!proxyInstances.TryGetValue(id, out var proxy))
            {
                // Proxy not running - config will be applied when it starts
                return Results.Accepted(value: new { message = "Modem not running. Settings will be applied when modem starts." });
            }

            proxy.OnConfigChanged(config);
            return Results.Ok(new { message = "Settings applied to modem." });
        });

        // Save config to file
        api.MapPost("/save", () =>
        {
            try
            {
                configManager.Save();
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // Serial ports enumeration
        api.MapGet("/serial-ports", () =>
        {
            var ports = SerialPortEnumerator.EnumerateNinoTncPorts().ToList();
            return Results.Json(ports, JsonOptions);
        });

        // NinoTNC modes
        api.MapGet("/nino-modes", () =>
        {
            var modes = KissFrameBuilder.NinoModes.Select(kv => new
            {
                mode = kv.Key,
                name = kv.Value
            });
            return Results.Json(modes, JsonOptions);
        });

        // Global config (web port, password, MQTT settings)
        api.MapGet("/global", () => new
        {
            webPort = configManager.Config.WebPort,
            hasPassword = !string.IsNullOrEmpty(configManager.Config.Password),
            mqttServer = configManager.Config.MqttServer,
            mqttUsername = configManager.Config.MqttUsername,
            hasMqttPassword = !string.IsNullOrEmpty(configManager.Config.MqttPassword)
        });

        api.MapPut("/global", async (HttpContext ctx) =>
        {
            try
            {
                var update = await ctx.Request.ReadFromJsonAsync<GlobalConfigUpdate>(JsonOptions);
                if (update == null)
                    return Results.BadRequest("Invalid update");

                configManager.UpdateGlobal(
                    update.WebPort,
                    update.Password,
                    update.MqttServer,
                    update.MqttUsername,
                    update.MqttPassword,
                    update.ClearMqttServer,
                    update.ClearMqttUsername,
                    update.ClearMqttPassword);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });
    }

    private static IResult HandleLogin(HttpContext ctx, ConfigManager configManager)
    {
        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Unauthorized();
        }

        var password = authHeader.Substring("Bearer ".Length);
        if (configManager.ValidatePassword(password))
        {
            return Results.Ok(new { success = true });
        }

        return Results.Unauthorized();
    }

    private static bool IsAuthenticated(HttpContext ctx, ConfigManager configManager)
    {
        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var password = authHeader.Substring("Bearer ".Length);
        return configManager.ValidatePassword(password);
    }

    private record GlobalConfigUpdate(
        int? WebPort,
        string? Password,
        string? MqttServer,
        string? MqttUsername,
        string? MqttPassword,
        bool ClearMqttServer = false,
        bool ClearMqttUsername = false,
        bool ClearMqttPassword = false);
}
