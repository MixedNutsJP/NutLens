# ADR-0026. 例外から利用者向けの文言への変換を 1 クラスに集める

- 状態: **採用**（2026-11-14 承認）
- 日付: 2026-11-14

## 背景

6 章（非機能要件）:

> エラー（キャプチャ対象が閉じられた、音声デバイスがない、claude が見つからない、
> JSON が壊れているなど）は、**原因が分かるメッセージ**で知らせる

いまは変換が 2 か所に散っている。

```csharp
// CaptureViewModel.ReportError
CaptureUnavailableException => Strings.ErrorCaptureUnavailable,
CaptureTargetLostException  => Strings.ErrorTargetLost,
…
```

```csharp
// AnalysisWorkflow.RunClaudeAsync — catch 節が 4 つ
catch (ClaudeNotFoundException exception) { … }
catch (ClaudeExecutionException) { … }
…
```

段階 4 で画面が 3 つ増える（設定・履歴・トレイ）。
**画面が増えるほど、同じ例外が画面ごとに違う文言になる。**
「claude が見つからない」が、撮影画面と設定画面で別の言い方になると、
利用者は別の不具合だと思う。

## 決定

### 1. `ExceptionMessageMapper`（`NutLens.App`）1 クラスに集める

```csharp
public interface IExceptionDescriber
{
    string Describe(Exception exception);
}
```

ViewModel は `this.describer.Describe(exception)` を呼ぶだけにする。
switch を ViewModel に書かない。

### 2. **Core の例外は表示用の文字列を持たない**

例外は**型**と**事実**だけを持つ。

```csharp
// 持ってよい：探したパスの一覧（事実）
public sealed class ClaudeNotFoundException(IReadOnlyList<string> searchedPaths) : …

// 持たせない：「Claude Code が見つかりませんでした。…」（表示の言葉）
```

表示の言葉は `NutLens.App/Resources/Strings.resx`（D-09）。
Core に表示文を書くと、**英語を足すときに Core を触ることになる。**

### 3. 知らない例外を握りつぶさない。型名を出す

```
予期しないエラーが発生しました。
InvalidOperationException: The operation is not valid due to the current state of the object.
```

**「エラーが発生しました」だけにしない。** 不具合報告から原因を追えなくなる。
型名を出すのは、利用者に理解させるためではなく、**報告に貼れるようにする**ため。

### 4. 要件が名指しした 4 つは必ず表に入れる

| 要件が挙げた状況 | 例外 | 文言の要点 |
|---|---|---|
| キャプチャ対象が閉じられた | `CaptureTargetLostException` | 撮影対象のウィンドウが閉じられました |
| 音声デバイスがない | （例外にしない） | **録音の失敗は警告。撮影は続く**（ADR-0016 決定 3） |
| claude が見つからない | `ClaudeNotFoundException` | 探した場所を**全部出す**。設定で指定できることも書く |
| JSON が壊れている | `SettingsCorruptedException` / 解析結果のパース失敗 | 退避先のパスを出す／生の出力を見られるようにする |

2 つめが表の中で例外になっている。**取れなかっただけで止めるものと、
止めないものを混ぜない**（概要設計 §10.1）。

### 5. 対応表は詳細設計書に置き、例外を足したら同じ作業で表も足す

表がコードと食い違うと、**表を読んだ人が「この例外は扱われている」と誤解する。**

## 影響

- `CaptureViewModel.ReportError` と `AnalysisWorkflow` の catch 節が 1 クラスへ移る
- `IExceptionDescriber` はテストできる（例外を渡して文字列を見るだけ）
- 新しい例外を足したら、対応表に 1 行足すまでが 1 つの作業

## 実装で分かったこと（2026-11-14）

### 1. `AnalysisWorkflow` の catch 節が 4 つから 1 つになった

```csharp
catch (Exception exception) when (exception is OperationCanceledException
    or ClaudeNotFoundException
    or ClaudeExecutionException
    or AnalyzerTemplateException)
{
    this.notifier.ShowError(this.describer.Describe(exception));
    return null;
}
```

**`when` で型を並べるのは残す。** `catch (Exception)` にすると、
想定していない例外まで「解析に失敗しました」として飲み込むことになる。
**握りつぶす場所を増やさない。**

### 2. `CaptureViewModel` から switch が消えた

`ReportError` は `this.describer.Describe(exception)` を呼ぶだけになった。

### 3. **影響の欄に書いた「テストできる」は嘘だった**

> `IExceptionDescriber` はテストできる（例外を渡して文字列を見るだけ）

**できない。** `ExceptionMessageMapper` は `NutLens.App`（`net10.0-windows`）にあり、
テストは `net10.0` なので参照できない（ADR-0002 の仕掛けがそのまま効く）。
`Strings.resx` を読むので、Core へ移すこともできない（決定 2 と衝突する）。

録音（ADR-0016）・資格情報（ADR-0020）と同じ種類の穴で、**目視でしか守れない。**
対応表（詳細設計 06 §7.2）を読み合わせるしかない。

決定そのものは変えない（1 箇所に集める利点は残る）が、
**「テストで守られている」という前提では読まないこと。**

## 宿題

- **`NutLens.Core` に日本語の警告文が残っている。**
  `MediaProcessingService` の「文字起こしを飛ばしました（…）」など。
  これは決定 2 と食い違っており、**段階 5 の英語化で効いてくる。**
  段階 4 では直さない（例外の変換とは別の筋で、まとめて動かすほうが安全）
- 文言の実際の見え方は**未目視**（画面をまだ見ていない）

## 却下した案

| 案 | 却下の理由 |
|---|---|
| 例外の `Message` をそのまま出す | .NET の既定の文面は英語で、原因が分からない（「オブジェクト参照がオブジェクト インスタンスに設定されていません」）。さらに **HTTP の例外には要求 URI が入ることがあり、そこには API キーが乗っている**（ADR-0020 決定 4） |
| 各 ViewModel で switch を書く（いまの形） | 画面が増えるほど、同じ例外が違う文言になる。**利用者は別の不具合だと思う** |
| 例外クラスに表示用の文字列を持たせる | `NutLens.Core` が UI の言語を持つことになる。D-09（文字列をリソースに分けて後から英語を足せるようにする）と衝突する（決定 2） |
| 知らない例外を「エラーが発生しました」でまとめる | **不具合報告から原因を追えない。** 利用者が貼れる情報が何も残らない（決定 3） |
| 例外を握りつぶして黙って続ける | 何が起きたか分からないまま結果が変わる。本プロジェクトの作法（黙って進めない）に反する |
| 変換を `NutLens.Core` に置く | Core は `Strings.resx` を知らない。知らせると層の向きが逆になる（ADR-0002） |
