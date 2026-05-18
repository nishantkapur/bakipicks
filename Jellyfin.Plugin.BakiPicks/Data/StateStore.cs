using System;
using System.IO;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BakiPicks.Data;

public class StateStore
{
    private readonly string _stateDir;
    private readonly string _statePath;
    private readonly string _seedsPath;
    private readonly ILogger<StateStore> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public StateStore(IApplicationPaths paths, ILogger<StateStore> logger)
    {
        _stateDir = Path.Combine(paths.PluginConfigurationsPath, "BakiPicks");
        Directory.CreateDirectory(_stateDir);
        _statePath = Path.Combine(_stateDir, "state.json");
        _seedsPath = Path.Combine(_stateDir, "taxonomy_seeds.json");
        _logger = logger;
    }

    public string StatePath => _statePath;

    public string SeedsPath => _seedsPath;

    public UserState LoadState()
    {
        if (!File.Exists(_statePath))
        {
            _logger.LogInformation("No state.json yet — returning empty state");
            return new UserState();
        }

        try
        {
            var json = File.ReadAllText(_statePath);
            var state = JsonSerializer.Deserialize<UserState>(json, JsonOpts);
            if (state == null || state.Version != 1)
            {
                _logger.LogWarning("state.json version mismatch or null — treating as empty");
                return new UserState();
            }
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read state.json — treating as empty");
            return new UserState();
        }
    }

    public void SaveState(UserState state)
    {
        state.LastBuiltAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(state, JsonOpts);
        var tmp = _statePath + ".tmp";
        File.WriteAllText(tmp, json);
        // File.Move with overwrite is atomic on POSIX, works on overlay/fuse filesystems,
        // and avoids File.Replace's PlatformNotSupportedException edge cases.
        File.Move(tmp, _statePath, overwrite: true);
        _logger.LogInformation("Wrote {Bytes} bytes to {Path}", json.Length, _statePath);
    }
}
