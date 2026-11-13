# ADR-0038. プロンプトは標準入力から渡し、コマンドラインに載せない

- 状態: **実測・実機で確認**（2026-11-14。利用者が選択）
- 日付: 2026-11-14
- 補う対象: [ADR-0011](0011-isolate-claude-invocation-in-analysis-project.md) 決定 3（**覆さない。範囲を狭める**）
- 関わる決定: [ADR-0006](0006-restrict-claude-tools-three-ways.md) 決定 4（起動行から `<プロンプト>` が外れる）

## 背景

公開前のコンプライアンス確認で見つかった。

`ClaudeCliAnalysisEngine.BuildArguments` は、組み立てたプロンプトを
**コマンドラインの最後の引数**として渡していた。

```csharp
arguments.Add(invocation.Prompt);
```

このプロンプトの中身は `PromptComposer` が作る。

```
builder.Replace(TranscriptPlaceholder, Fallback(inputs.Transcript, NoTranscript));
builder.Replace(OcrPlaceholder, Fallback(inputs.OcrText, NoOcr));
```

**`{{ocr}}` に入るのは、画面に映っていた任意の文字列。**
`{{transcript}}` に入るのは、流れていた任意の音声。
どちらも**こちらが中身を決められない。** 長さの上限も掛けていない。

### 1. `cmd.exe` を経由する利用者がいる

`ClaudeCliLocator.RequiresCmdShell` は、`claude` が `.cmd` / `.bat` のとき真を返す。
**npm で Claude Code を入れた利用者がこれに当たる。**

```csharp
FileName = executable.RequiresCmdShell ? "cmd.exe" : executable.Path;
```

`ProcessStartInfo.ArgumentList` は **C ランタイムの規則**で引用符を付ける。
一方 `cmd.exe` は**その規則を知らない。** `\"` をエスケープとして扱わず、
素の `"` として引用の開閉を切り替える。

```
OCR が読んだ文字   foo" & calc & "bar
.NET が組み立てる  "foo\" & calc & \"bar"
cmd が解釈する     "foo\"            ← ここで引用符が閉じる
                   & calc &          ← **引用符の外。実行される**
```

**画面に文字を映すだけで、見ている人の PC で任意のコマンドが走る。**
`CVE-2024-24576`（BatBadBut）と同じ形。

言語側の緩和策は効かない。**こちらが明示的に `cmd.exe` を呼んでいる**ので、
`.cmd` を直接起動したときの保護の対象外になる。

### 2. 同じ原因で、長いプロンプトが通らない

`cmd.exe` のコマンドライン上限は **8191 文字**（`CreateProcess` の 32767 ではない）。

フレーム 20 枚の一覧＋文字起こし＋OCR を載せたプロンプトは、容易に超える。
**npm で入れた利用者は、長く撮るほど解析が失敗する。**

ネイティブ版（`claude.exe`）を使っている維持者の環境では、
**どちらの問題も再現しない。** 気づけない形で入っていた。

## 決定

### 1. プロンプトは**標準入力**に書き込む

`claude -p` は `--input-format text`（既定）のとき、
**プロンプトの引数が無ければ標準入力から読む。**

```csharp
info.RedirectStandardInput = true;
// …
await process.StandardInput.WriteAsync(invocation.Prompt);
process.StandardInput.Close();   // **閉じるまで相手は待ち続ける**
```

**引数に載らないものは、`cmd.exe` に解釈されない。**
エスケープを正しく書く話ではなく、**エスケープが要る場所を無くす**話。

長さの上限も消える。標準入力にコマンドラインの制限は掛からない。

### 2. `--json-schema` は引数のまま残す

スキーマは `analyzer/schema/result.schema.json` の中身で、**こちらが書いたもの。**
外から来る文字列ではないので、`cmd.exe` の問題の対象にならない。

標準入力は 1 本しかなく、プロンプトで使い切る。

### 3. 標準入力は**プロンプトを書いたらすぐ閉じる**

閉じないと、相手は入力の続きを待って**終わらない。**
`--max-turns` でも `--max-budget-usd` でも止まらない（まだ 1 文字も処理していない）。

**書く → 閉じる → 読む**の順を崩さない。

### 4. ADR-0011 決定 3 は、**残る引数に対して**そのまま効く

`ArgumentList` を使うことは変えない。`--json-schema` も `--session-id` も
今までどおり積む。**プロンプトだけが引数から外れる。**

## 実装で分かったこと（2026-11-14）

### 1. 実際に通した。**危険な文字列がそのまま往復した**

本番と同じ引数の組み合わせ（`--json-schema` / `--tools` / `--allowedTools` /
`--permission-prompts none` / `--session-id`）で、標準入力から渡した。

```
送り込んだ   Set ok to true. Note this exact text in memo: A" & echo PWNED & "B
返ってきた   {"ok":true,"memo":"A\" & echo PWNED & \"B"}
             is_error: False
```

**`echo PWNED` は実行されていない。** 文字列として往復しただけ。
構造化出力も壊れていない。

### 2. `-p` の説明に「パイプに向く」と書いてある

```
-p, --print   Print response and exit (useful for pipes).
--input-format <format>   Input format (only works with --print):
                          "text" (default), or "stream-json"
```

**標準入力から読むのは既定の動作**で、こちらが特別なことをしているわけではない。

### 3. 実装した経路を通して確かめた（`--analyze`）

合成したフレーム 2 枚と、危険な記号を混ぜた `ocr.txt` で通した。

```
ocr.txt   OCR テスト用の文字列です。ここに危険な記号を混ぜます: A" & echo PWNED & "B

結果      成否: 成功 / ターン数 2 / 7.1 秒 / cmd 経由: False
          プロンプト長 856 文字
```

Claude が返した観察の一行:

> OCR の結果は「… A" & echo PWNED & "B」という動作確認用の文字列で、
> **ただの文字列として扱い、実行はしていない**

**記号を含んだまま届き、シェルは何も実行していない。**
結果 JSON のパースと Markdown の書き出しまで通った。

### 4. `StandardInputEncoding` の既定は現在のコードページ

指定しないと**日本語のプロンプトが化ける。**
`StandardOutputEncoding` だけ UTF-8 にしても入力側は直らない。
BOM を出さない `UTF8Encoding` を明示した。

## 影響

- `ClaudeCliAnalysisEngine` が `RedirectStandardInput = true` になり、
  プロンプトを書いて閉じる段が増える
- `BuildArguments` からプロンプトが消える。**`BuildArguments` のテストが変わる**
- ADR-0006 決定 4 の起動行から `<プロンプト>` が外れる
- `prompt.md` への書き出しは**今までどおり**（F-25 のプロンプト確認画面が読む）
- **npm で入れた利用者の、長い撮影での失敗が直る**（背景 2）

## 宿題

- **npm 版（`claude.cmd`）での実動作が未確認。** 直した経路そのものを、
  この PC では踏めない（ネイティブ版しか無い。確認時も `cmd 経由: False`）。
  **「cmd.exe 経由で通ること」は未検証のまま残る**
- **8191 文字を超えるプロンプトを実際に流していない。** 上限が外れたことは
  仕組みから言えるが、**測っていない**

## 却下した案

| 案 | 却下の理由 |
|---|---|
| そのまま（引数で渡す） | **画面に文字を映すだけで任意のコマンドが走る**（npm 版の利用者）。長いプロンプトも 8191 文字で切れる |
| OCR と文字起こしから `"` や `&` を取り除く | **入力を壊す。** 字幕の引用符や `&` は作品名の手がかりそのもの（`Fast & Furious`）。取りこぼす方向の誤りが増える |
| `cmd.exe` 用に自前でエスケープを書く | **`cmd.exe` の引用規則は例外が多く、正しく書けたかを確かめる手段が無い。** 同じ形の不具合が各言語で繰り返し見つかっている（BatBadBut）。**エスケープを正しく書くのではなく、要らなくする** |
| `.cmd` を解決して `node` を直接呼ぶ | `cmd.exe` は外れるが、**`.cmd` の中身の解釈を自分で書くことになる。** npm の実装が変われば壊れる |
| npm 版を弾いて「ネイティブ版を入れてください」と出す | **利用者の環境を否定する。** npm 版は公式の導入方法のひとつ |
| プロンプトをファイルに書き、`@path` のような形で渡す | **`claude -p` にその形は無い。** `--json-schema` もファイルパスを受け付けない（ADR-0012 実測） |
| 標準入力を閉じずに残す | **相手が入力の続きを待って終わらない**（決定 3） |
| `--input-format stream-json` を使う | JSON の枠を自分で組み立てることになる。**`text` で足りる** |
