# ADR-0001. ターゲットフレームワークを net10.0-windows10.0.19041.0 にする

- 状態: **実測・実機で確認**（2026-11-13 承認 / 同日ビルド・実行を確認）
- 日付: 2026-11-13

## 背景

`implementation-prompt.md` 3 章は「C# / .NET 8、WPF を推奨。別の構成の方がよいと判断した場合は、
理由を添えて提案すること」と書いている。提案の可否を判断するため、開発機の状態と各版のサポート期限を調べた。

開発機の SDK（`dotnet --list-sdks` の実測）:

```
9.0.320 [C:\Program Files\dotnet\sdk]
10.0.402 [C:\Program Files\dotnet\sdk]
10.0.403 [C:\Program Files\dotnet\sdk]
```

**.NET 8 SDK は入っていない。**

サポート期限:

| 版 | 区分 | サポート終了 |
|---|---|---|
| .NET 8 | LTS | **2026 年 11 月**（＝この ADR を書いている月） |
| .NET 9 | STS | 2026 年 5 月（終了済み） |
| .NET 10 | LTS | 2028 年 11 月 |

このツールは GitHub で一般公開し、Velopack で自動更新しながら長く配布する（10 章）。
配布開始の時点でサポートが切れている版を土台にすると、以後のセキュリティ修正を取り込めない。

## 決定

### 1. 全プロジェクトの土台を .NET 10 にする

`Directory.Build.props` で `LangVersion` を `latest`、`Nullable` を `enable`、
`TreatWarningsAsErrors` を `true` にし、各プロジェクトの TFM を次のとおりにする。

| プロジェクト | TFM | 理由 |
|---|---|---|
| `NutLens.Core` | `net10.0` | Windows 非依存。テストを素早く回すため OS 依存を持たせない |
| `NutLens.Core.Tests` | `net10.0` | 同上 |
| `NutLens.Capture` | `net10.0-windows10.0.19041.0` | WinRT 射影（`Windows.Graphics.Capture` / `Windows.Media.Ocr`）が必要 |
| `NutLens.App` | `net10.0-windows10.0.19041.0` | WPF ＋ WinRT 射影 |

### 2. Windows SDK の下限を 10.0.19041.0 に置く

`Windows.Graphics.Capture` 自体は 10.0.17763 から使えるが、
`GraphicsCaptureSession.IsBorderRequired`（黄色い枠を消す）は 10.0.22000 以降にしかない。
下限を 19041（Windows 10 2004）にしておき、**22000 以降の API は `ApiInformation` で存在を確かめてから触る**。
対象 OS は Windows 11 だが、下限を上げすぎると WinRT 射影の参照が将来の SDK 更新に引きずられるため 19041 で止める。

### 3. 配布は自己完結（self-contained）で行う

利用者に .NET ランタイムを別途入れさせない。Velopack のパッケージに含める（詳細は段階 5 の ADR）。

## 実装で分かったこと（2026-11-13）

### 1. Windows SDK 射影の版は 22621 に上げた（決定 2 の修正）

下限を 19041 にすると、**`GraphicsCaptureSession.IsBorderRequired` が射影に存在せず、
`ApiInformation` で囲ってもコンパイルが通らない。** 実行時に存在を確かめる前に、
型として参照できないという問題だった。

対応：

| 項目 | 値 |
|---|---|
| `TargetFramework` | `net10.0-windows10.0.**22621**.0` |
| `SupportedOSPlatformVersion` | `10.0.19041.0`（**実行時の下限は変えない**） |
| 22000 以降の API の守り方 | `ApiInformation` ではなく **`OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)`** |

`ApiInformation` をやめたのは、プラットフォーム互換性の解析器（CA1416）が
`OperatingSystem` の判定しか理解しないため。`ApiInformation` のままだと
「下限 19041 なのに 22000 の API を触っている」という警告が消えず、
`TreatWarningsAsErrors` でビルドが通らない。

### 2. .NET 10 SDK のソリューションと `dotnet test`

- `dotnet new sln` が作るのは **`.slnx`**（`.sln` ではない）
- `dotnet test` は VSTest を使わなくなった。**`global.json` に
  `"test": { "runner": "Microsoft.Testing.Platform" }` が要る。**
  無いと「Testing with VSTest target is no longer supported」で失敗する。
  `dotnet.config` に書いても効かなかった（実測）

### 3. 実機で確認したこと

```
dotnet build NutLens.slnx -c Debug   → 0 警告 0 エラー
dotnet test                          → 78 件すべて成功
NutLens.exe                          → 起動、タイトル「NutLens — ショート動画の作品特定」
```

## 影響

- プロンプトの「.NET 8」から外れるため、この ADR が理由の記録になる
- 開発機に追加のインストールが要らない（SDK 10.0.403 が既にある）
- `net10.0` と `net10.0-windows` が混在するので、`Core` に Windows 専用 API を書くとビルドが落ちる。
  これは層の分離（ADR-0002）を**コンパイラに守らせる**ことになるので、むしろ利点として扱う

## 却下した案

| 案 | 却下の理由 |
|---|---|
| net8.0-windows（プロンプト記載どおり） | **配布開始月にサポートが終わる。** 以後のセキュリティ修正を取り込めない。加えて開発機に .NET 8 SDK が無く、ローカル実行には .NET 8 Desktop Runtime の別途インストールが必要になる |
| net9.0-windows | STS で既にサポート終了。net8 より短命 |
| .NET Framework 4.8 | WinRT 射影（`Windows.Graphics.Capture`）の利用に `Microsoft.Windows.SDK.Contracts` 経由の追加設定が必要で、`System.Threading.Channels` や `record` も使えない。自己完結配布もできない |
| net10.0-windows10.0.22621.0（下限を上げる） | Windows 10 2004〜21H2 を切る必要がない。`IsBorderRequired` は `ApiInformation` で足りる（決定 2） |
| マルチターゲット（net8.0 と net10.0 の両方） | 個人用ツールに二重のビルド・検証コストを払う理由がない。**どちらで落ちたか分からない不具合報告**が増える |
