# ADR-0002. 層を Core / Capture / App に割り、外界に触る実装をインタフェースの背後に置く

- 状態: **実測・実機で確認**（2026-11-13 承認 / 同日に実装して確認）
- 日付: 2026-11-13

## 背景

このツールが触る外界は多い。

- 画面キャプチャ（Windows.Graphics.Capture + Direct3D 11）
- システム音声（WASAPI ループバック）
- 文字起こし（whisper の native ライブラリ）
- OCR（Windows.Media.Ocr、言語パックの有無に依存）
- 外部プロセス（`claude -p`）
- ファイルシステム（`%LOCALAPPDATA%` の一時フォルダ）
- 任意で HTTP（Google Cloud Vision）

一方、`implementation-prompt.md` 3 章 4 項は**単体テストを成果物として要求**している
（重複除去、間引き、プロンプト生成、結果 JSON のパース）。
これらは上の外界と本質的に無関係な純ロジックだが、1 プロジェクトに全部混ぜると
「テストを動かすためにグラフィックスデバイスと音声デバイスと `claude` が必要」になる。

## 決定

### 1. プロジェクトを 3 つに割り、参照方向を一方向にする

```
NutLens.App  ──▶  NutLens.Capture  ──▶  NutLens.Core
   (WPF)            (WinRT / Win32)        (純ロジック・抽象)
```

逆向きの参照はしない。`NutLens.Core` の TFM を `net10.0`（Windows 無し）にすることで、
**Core に Windows 専用 API を書いた瞬間にビルドが落ちる**（ADR-0001 決定 1）。
規約を人の注意力ではなくコンパイラに守らせる。

### 2. 外界に触るものは必ず `NutLens.Core` 側のインタフェースを実装する形にする

抽象は `Core` に置き、実装は `Capture`（または段階 2 以降で `Analysis` 等）に置く。
段階 1 で定義する抽象:

| 抽象（Core） | 実装（Capture） |
|---|---|
| `ICaptureTargetProvider` | `WindowCaptureTargetProvider` / `MonitorCaptureTargetProvider` |
| `IFrameSource` | `WgcFrameSource`（＋切り出し用の `CroppingFrameSource`） |
| `IFrameEncoder` | `WicFrameEncoder` |
| `IDuplicateFrameFilter` | `DHashDuplicateFrameFilter`（Core 内。純ロジックなので例外的に Core に実装を置く） |
| `IFrameThinner` | `EvenTimeFrameThinner`（同上） |
| `ISessionWorkspace` | `FileSystemSessionWorkspace`（Core 内。`System.IO` は Windows 非依存） |
| `ISettingsStore` | `JsonSettingsStore`（同上） |
| `ISystemClock` | `SystemClock`（時間を抽象化してテストで固定する） |

**純ロジックおよび `System.IO` / `System.Text.Json` だけで書けるものは実装も Core に置く。**
`Capture` に置くのは「WinRT・Win32・WPF・native ライブラリに触るもの」に限る。
この線引きを守れば、テスト対象が自然に Core に集まる。

### 3. 束ねる役を `CaptureSessionService` 1 クラスに閉じ込める

1 回の撮影の流れ（開始 → フレーム受信 → 重複判定 → 符号化 → 停止 → 間引き → 書き出し）は
`NutLens.Core.Capture.CaptureSessionService` が持つ。
このクラスは上記の抽象にしか依存しないので、偽の `IFrameSource` / `IFrameEncoder` / `ISystemClock` を
差し込めば**グラフィックスデバイス無しで撮影の流れ全体をテストできる**。

### 4. 具体型を知る場所を合成ルート 1 箇所に限る

`NutLens.App` の `App.xaml.cs` で `Microsoft.Extensions.DependencyInjection` の
`ServiceCollection` を組み立てる。ViewModel は抽象だけを受け取る。
`new` で具体型を直に作るコードは合成ルート以外に書かない。

## 実装で分かったこと（2026-11-13）

### 1. 「外界に触るか否か」で割る線引きが実際に効いた

当初は `CroppingFrameSource`（画面の矩形の切り出し）を `NutLens.Capture` に置いていた。
書いてみると**画素の並べ替えだけで Windows に一切触らない**と分かり、`NutLens.Core` へ移した。
結果、矩形の切り出し（寸法・画素の一致・クランプ・負座標・デコレータの委譲）を
単体テスト 9 件で固定できた。Capture に置いたままなら、どれもテストできていない。

同じ理由で `DeviceOwningFrameSource`（Direct3D デバイスの寿命を持つデコレータ）は
Capture に残した。こちらは外界そのものを持つ。

### 2. 偽物を差し込むテストが実際に書けた

`CaptureSessionServiceTests` は `FakeFrameSource` / `FakeFrameEncoder` を差し込み、
グラフィックスデバイス無しで次を確かめている。

- 撮影時間で止まる／手動停止でもそれまでの分を返す
- 重複除去と間引きが連携する／メモリ上限でその場の間引きが起きる
- 撮影開始時に前回の一時データが消える（要件 5.5）
- フレーム源が必ず破棄される

### 3. `Func<int, IDuplicateFrameFilter>` ではなくインタフェースにした

当初の設計ではフィルタの生成を `Func<int, IDuplicateFrameFilter>` で渡す想定だった。
DI コンテナにデリゲートを登録する形は読み手に意図が伝わりにくいので、
`IDuplicateFrameFilterFactory` を定義した。登録も `AddSingleton<IDuplicateFrameFilterFactory, …>` で揃う。

## 影響

- プロジェクトが 4 つ（テスト込み）になり、`dotnet new` 相当の初期作業が増える
- `IFrameSource` が `IAsyncEnumerable<CapturedFrame>` を返す設計になるため、
  WGC のイベント駆動（`FrameArrived`）を `System.Threading.Channels` で橋渡しする一手間が入る。
  その代わり、上位層は「フレームが流れてくる列」としてだけ扱えるようになる
- 段階 2〜5 で足す機能（`IAnalysisEngine` / `ITranscriber` / `IOcrEngine` / `IWebDetectionClient` /
  `IHistoryStore`）も同じ流儀に乗る。置き場の判断に迷わない

## 却下した案

| 案 | 却下の理由 |
|---|---|
| 1 プロジェクト（WPF アプリのみ）に全部入れる | **テストの実行にグラフィックスデバイスと音声デバイスが必要になる。** CI（段階 5、GitHub Actions の windows ランナー）でキャプチャ系の初期化が走ると、仮想環境の都合で落ちたり落ちなかったりする不安定なテストになる |
| Core / App の 2 層にし、WinRT 実装も Core に入れる | Core の TFM を `net10.0-windows` にせざるを得ず、決定 1 の「コンパイラに守らせる」効果が消える。結果として純ロジックに Windows 依存が混ざるのを止める手段が無くなる |
| 機能ごとにプロジェクトを割る（Capture / Audio / Ocr / Analysis / History …） | 段階 1 時点で 7〜8 プロジェクトになり、参照関係の管理コストが機能の規模に見合わない。**外界の種類ごとではなく「外界に触るか否か」で割る**方が境界が安定する |
| インタフェースを作らず、具体型を直に使って `#if` でテスト時だけ差し替える | テスト用の分岐が製品コードに残る。差し替え漏れが**テストだけ通って実機で落ちる**形で表れる |
| DI コンテナを使わず手で `new` を積む | 段階 4 までに依存が 15 個前後まで増える見込みで、ViewModel の生成箇所が増えるたびに配線を手で直す必要が出る。合成ルートの一元化（決定 4）が崩れる |
