using System.Text.Json;
using System.Text.Json.Serialization;

namespace DSCC.Core.Configuration;

public sealed class DsccConfigStore
{
    public static JsonSerializerOptions DefaultJsonOptions { get; } = CreateDefaultJsonOptions();

    public DsccConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<DsccConfig>(stream, DefaultJsonOptions)
            ?? throw new InvalidDataException($"Config file '{path}' did not contain a valid DSCC config.");
    }

    public async Task<DsccConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DsccConfig>(stream, DefaultJsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Config file '{path}' did not contain a valid DSCC config.");
    }

    public void Save(string path, DsccConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);

        EnsureDirectory(path);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, config, DefaultJsonOptions);
    }

    public async Task SaveAsync(string path, DsccConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);

        EnsureDirectory(path);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, config, DefaultJsonOptions, cancellationToken);
    }

    private static JsonSerializerOptions CreateDefaultJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
