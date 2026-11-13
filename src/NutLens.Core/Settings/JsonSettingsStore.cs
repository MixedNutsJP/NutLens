using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NutLens.Core.Diagnostics;
using NutLens.Core.Sessions;

namespace NutLens.Core.Settings;

/// <summary>設定の読み書き（6 章）。</summary>
public interface ISettingsStore
{
    /// <summary>
    /// 読み込む。ファイルが無ければ既定値。
    /// 壊れていた場合は <see cref="SettingsCorruptedException"/> を投げる前に
    /// 壊れたファイルを退避する（呼び出し側は既定値で続行してよい）。
    /// </summary>
    NutLensSettings Load();

    void Save(NutLensSettings settings);
}

/// <summary>
/// System.Text.Json のソース生成を使う逆直列化の文脈。
/// リフレクションに頼らないことで、段階 5 でトリミング／AOT を選べる余地を残す。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(NutLensSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}

/// <inheritdoc />
public sealed class JsonSettingsStore : ISettingsStore
{
    private const string TemporaryExtension = ".tmp";
    private const string BrokenExtension = ".broken";

    private readonly IAppDataPaths paths;

    public JsonSettingsStore(IAppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.paths = paths;
    }

    public NutLensSettings Load()
    {
        string path = this.paths.SettingsFile;

        if (!File.Exists(path))
        {
            return new NutLensSettings();
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            throw new SettingsCorruptedException(
                $"設定ファイルを読めませんでした: {path}",
                path,
                exception);
        }

        try
        {
            NutLensSettings? loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.NutLensSettings);
            if (loaded is null)
            {
                throw new JsonException("設定ファイルの中身が null でした。");
            }

            // 版の移行は読み込み時だけ。保存時に呼ぶと、移行が二重にかかる。
            return loaded.Migrate().Normalize();
        }
        catch (JsonException exception)
        {
            // 壊れたファイルは消さずに退避する。利用者が中身を確かめられるようにするため。
            string backup = path + BrokenExtension;
            TryMove(path, backup);

            throw new SettingsCorruptedException(
                $"設定ファイルが壊れていたため、既定値で起動しました。壊れたファイルは {backup} に退避しました。",
                backup,
                exception);
        }
    }

    public void Save(NutLensSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string path = this.paths.SettingsFile;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            settings.Normalize(),
            SettingsJsonContext.Default.NutLensSettings);

        // アトミックに置き換える。
        // 直接上書きすると、書き込み中にプロセスが落ちたとき設定ファイルが壊れる。
        string temporary = path + TemporaryExtension;
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    private static void TryMove(string source, string destination)
    {
        try
        {
            File.Move(source, destination, overwrite: true);
        }
        catch (IOException)
        {
            // 退避できなくても、既定値での続行は妨げない。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
