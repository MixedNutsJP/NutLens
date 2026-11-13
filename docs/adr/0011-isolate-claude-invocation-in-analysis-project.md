# ADR-0011. Claude Code を起動する実装を NutLens.Analysis に分ける

- 状態: **採用**（2026-11-13）
- 日付: 2026-11-13

## 背景

段階 2 で「外部の実行ファイルを起動する」コードが入る。ADR-0002 の層分けでは、

- `NutLens.Core` … 純ロジックと抽象（`System.IO` / `System.Text.Json` までは実装も置く）
- `NutLens.Capture` … WinRT / Win32 / WPF / native に触るもの

となっている。`System.Diagnostics.Process` は Windows 専用 API ではないので、
素直に読むと Core に置けてしまう。実際、Core には既に `FileSystemSessionWorkspace`（ファイル削除）がある。

しかし「任意の実行ファイルを起動する」は、ファイル入出力とは危険度が違う。

- 起動する実行ファイルのパスは**設定から来る**（D-05：固定で書かない）。
  つまり利用者が指定した任意の exe を起動しうる
- 渡す引数にはプロンプト全文（利用者のメモを含む）が乗る
- 標準出力をパースして画面に出す

D-14（秘密情報を置かない）や D-08（外部送信は Claude Code だけ）を後から点検するとき、
**「どこを読めば外部プロセスの起動が全部見えるか」が 1 箇所に決まっている**ほうがよい。

## 決定

### 1. `NutLens.Analysis`（`net10.0`）を作る

```
NutLens.App  →  NutLens.Capture  →  NutLens.Core
     \         →  NutLens.Analysis  ↗
```

`-windows` は付けない。`Process` も `System.Text.Json` も Windows に依存しないので、
Core と同じく**ビルドの制約でプラットフォーム依存の混入を止める**。

入れるもの:

| クラス | 責務 |
|---|---|
| `ClaudeCliLocator` | `claude` の実行ファイルを探し、`--version` で動作を確かめる（D-05） |
| `ClaudeCliAnalysisEngine` | `claude -p` を起動し、標準出力を受け取り、`claude.log` を書く |
| `AnalyzerTemplateDeployer` | `analyzer/` をセッションフォルダへ複製する（F-23） |

抽象（`IAnalysisEngine` / `IClaudeCliLocator` / `IAnalyzerTemplateDeployer`）は Core に置く。
プロンプト生成と結果のパースは純ロジックなので Core に置く。

### 2. 外部プロセスの起動は、このプロジェクトの 1 クラスだけが行う

`Process.Start` を書いてよいのは `ClaudeCliAnalysisEngine` と `ClaudeCliLocator` だけ。
例外は `CaptureViewModel.OpenFolder`（エクスプローラで保存先を開く）で、
こちらは `UseShellExecute = true` でフォルダパスだけを渡す。

### 3. 起動の引数は `ProcessStartInfo.ArgumentList` に積む

> **注記（2026-11-14）.** この決定は**残る引数に対してそのまま効く**が、
> **プロンプト本文だけは引数から外れた**
> （[ADR-0038](0038-send-the-prompt-over-stdin-not-as-an-argument.md)）。
> `ArgumentList` は `cmd.exe` 経由のときに守ってくれない。


文字列を連結して渡さない。`--json-schema` にはスキーマの JSON がそのまま乗るので、
引用符のエスケープを自分でやると**プロンプトの内容次第で壊れる**形の不具合になる。

## 影響

- プロジェクトが 5 つになる
- `NutLens.Analysis` は `net10.0`。Windows 専用 API を書くとビルドが落ちる
- 段階 3 の後に足す Web Detection（HTTP で Google に送る）も、同じ理由でここに置く。
  **外部へ出ていく経路がこのプロジェクトに揃う**

## 却下した案

| 案 | 却下の理由 |
|---|---|
| `NutLens.Core` に入れる | 「どこで外部プロセスが起動されるか」が純ロジックに紛れる。Core はテストで何百回も実行される場所なので、**うっかり本物の `claude` を起動するテストを書いてしまう**余地を残したくない |
| `NutLens.Capture` に入れる | 名前が「画面を撮る」層を指しており、`claude` の起動が入ると何の層か読めなくなる。段階 3 で音声・OCR が入ると、さらに雑多になる |
| `NutLens.App` に入れる | ViewModel から直に `Process.Start` を呼ぶ形になり、単体テストができなくなる。合成ルート以外が具体型を知る（ADR-0002 決定 4 に反する） |
| 起動を抽象化せず、`IAnalysisEngine` の実装を直に DI する | それは今回やっていること。抽象は Core、実装は Analysis に分けているので矛盾しない |
| 引数を文字列連結で組み立てる | プロンプトに `"` が含まれた瞬間に壊れる。**壊れるのは「利用者が引用符つきのメモを書いたとき」**で、原因が最も分かりにくい形になる |
