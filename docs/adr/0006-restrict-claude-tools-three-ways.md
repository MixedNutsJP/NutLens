# ADR-0006. Claude Code の権限を `--tools` と `--permission-prompts none` で三重に絞る

- 状態: **実測・実機で確認**（2026-11-13 承認 / 同日に `claude -p` を実際に叩いて確認）
- 日付: 2026-11-13

## 背景

5.4 章は次を要求している。

> 許可するツールは `--allowedTools "Read,WebSearch,WebFetch"` に絞る。書き込みとシェルは許可しない

意図は「書き込みとシェルを使わせない」ことだが、指定された手段がその意図を満たすかを確かめた。
開発機の Claude Code v2.1.322 の `claude --help` から、関係する 3 つのオプション（実測）:

```
--allowedTools, --allowed-tools <tools...>
    Comma or space-separated list of tool names to allow (e.g. "Bash(git *) Edit")

--tools <tools...>
    Specify the list of available tools from the built-in set. Use "" to disable all
    tools, "default" to use the default set, or specify tool names (e.g. "Bash,Edit,Read").

--permission-prompts <target>
    Who answers permission prompts with --print: "host" (the SDK host or
    --permission-prompt-tool) or "none" (nobody: anything that would prompt is
    denied automatically; the permission mode still decides everything else)
    (choices: "host", "none", default: "host")
```

**`--allowedTools` は「許可リスト」であって「利用可能なツール集合の指定」ではない。**
Bash や Write は**モデルから見えたまま**で、許可されていないので呼べば止まる、という形になる。
対して `--tools` は組み込みツール集合そのものを絞る。

さらに `--permission-prompts` の既定は `host` で、`-p`（非対話）では
ホスト（＝このアプリ）が応答することになっている。アプリが応答器を提供しなければ、
その先の挙動は Claude Code の版に依存する。**版に依存する既定に安全を預けたくない。**

## 決定

### 1. `--tools` でツール集合そのものを絞る

```
--tools "Read,WebSearch,WebFetch"
```

Bash・Write・Edit・NotebookEdit 等はモデルに提示されない。

### 2. `--allowedTools` も併せて渡す（5.4 の要求どおり）

```
--allowedTools "Read,WebSearch,WebFetch"
```

決定 1 と重複するが、仕様が明示している指定なので残す。
`--tools` が将来の版で挙動を変えても、許可リスト側が残る。

### 3. `--permission-prompts none` を渡す

問い合わせが必要な操作は、誰にも聞かずに**自動的に拒否**される。
アプリ側に権限応答の UI を持たない以上、これが正しい既定。
「アプリが黙って許可してしまう」経路を作らない。

### 4. 起動行をまとめる

> **注記（2026-11-14）.** 下の起動行の最後にある `<プロンプト本文>` は、
> [ADR-0038](0038-send-the-prompt-over-stdin-not-as-an-argument.md) で**引数から外れ、
> 標準入力に移った。** ツールの絞り方（決定 1〜3）はそのまま効く。


```
claude -p
  --output-format json
  --json-schema <result.schema.json の中身>
  --tools "Read,WebSearch,WebFetch"
  --allowedTools "Read,WebSearch,WebFetch"
  --permission-prompts none
  --session-id <自前発番の UUID>          ← ADR-0007
  --max-turns 30
```

### 5. `--max-turns` の既定値を 30 にする

**`--max-turns` は v2.1.322 の `--help` に載っていない。** 実際に渡すと受理されることは確認した
（未知のオプションならコマンドラインパーサがエラーを返すが、返さなかった）。
隠しオプションに依存しているため、**将来の版で消える可能性がある**。
設定で変更可能にし（5.4 の要求）、渡した結果エラーになった場合は
このオプション抜きで再実行するフォールバックを入れる。

30 とした根拠：SKILL.md の手順は「全フレームを Read」「Web 検索（英語・日本語）」
「候補ごとの照合」の 3 段で、フレーム 20 枚＋検索数回＋ページ取得数回を見込むと
20〜25 ターン程度。余裕を見て 30。**これは見積もりであり、実測していない。**

### 6. 作業ディレクトリをセッションフォルダに固定し、`--add-dir` を渡さない

`claude -p` の起動時の作業ディレクトリをセッションフォルダ
（`%LOCALAPPDATA%\NutLens\work\<セッションID>\`）にする。
フレーム・文字起こし・OCR はすべてその下にあるので、Read は作業ディレクトリの中で完結する。
**`--add-dir` を渡さない**ことで、Read の到達範囲をセッションフォルダに閉じる。

## 実装で分かったこと（2026-11-13、Claude Code 2.1.322）

### 1. 起動行は全部そのまま通った

```
claude -p --output-format json --json-schema '<スキーマ>' \
  --tools "Read" --allowedTools "Read" --permission-prompts none \
  --max-turns 5 --session-id <UUID> "<プロンプト>"
→ 終了コード 0、"is_error": false
```

`--max-turns`（ヘルプ非掲載）も拒否されなかった。

### 2. `--json-schema` は**インライン JSON のみ**。ファイルパスは受け付けない

```
claude … --json-schema schema.json
→ Error: --json-schema is not valid JSON: JSON Parse error: Unexpected identifier "schema"
```

`result.schema.json` の**中身を読み込んで渡す**必要がある。
引数の引用符が問題になるので、`ProcessStartInfo.ArgumentList` に積む（シェルを通さない）。

### 3. `--output-format json` の出力の形（宿題の回答）

結果は **2 箇所**に入っていた。

| フィールド | 型 | 中身 |
|---|---|---|
| **`structured_output`** | **オブジェクト** | スキーマに従った結果そのもの。**これを使う** |
| `result` | 文字列 | 同じ内容を JSON 文字列にしたもの。予備 |

他に使えるもの:

| フィールド | 用途 |
|---|---|
| `is_error`（bool）/ `subtype`（"success"） | 成否の判定 |
| `session_id` | 照合（ADR-0007） |
| `num_turns` | `--max-turns` に当たったかの目安 |
| `stop_reason` / `terminal_reason` | 打ち切りの理由 |
| `total_cost_usd` | 利用者に見せると親切（D-07 の「自分の利用枠を使う」の実感） |
| `permission_denials`（配列） | 絞り込みが効いているかの確認に使える |
| `credential_expires_at` | サインインの期限。初回セットアップの案内に使える |

## 影響

- Claude はファイルを書けないので、結果は標準出力の JSON だけで受け取る（`--output-format json`）
- Bash が無いため、Claude 側で画像を加工・変換することはできない。
  アプリ側で「そのまま読める JPEG」を用意する責任が増える（ADR-0008）
- `--max-turns` が将来消えたとき、フォールバックが無いと**全ての解析が起動直後に失敗する**。
  決定 5 のフォールバックは必須

## 宿題

- `--max-turns 30` が実際の解析（フレーム 20 枚＋Web 検索）で足りるか。足りないと結果が返らない。
  段階 2 の通し確認で `num_turns` を見て決める
- `--json-schema` に渡すスキーマの長さ。Windows のコマンドライン長は 32767 文字。
  `result.schema.json` は 2 KB 程度の見込みで余裕があるが、実物で確認する

### 片付いた宿題

- ~~`--output-format json` での構造化結果の入り方~~ → `structured_output`（オブジェクト）と
  `result`（文字列）の両方に入る。前者を使う（実装で分かったこと 3）
- ~~`--json-schema` はファイルパスを受けるか~~ → **受けない。インライン JSON のみ**（同 2）

## 却下した案

| 案 | 却下の理由 |
|---|---|
| `--allowedTools` だけを渡す（5.4 の記載どおり） | **Bash・Write がモデルに提示されたまま残る。** 許可されていないので実行は止まるが、「止まる」ことの保証は権限判定側の実装に依存する。ツール自体を消す手段（`--tools`）がある以上、それを使わない理由がない |
| `--restricted` を使う | コマンドを実行するツールと WebFetch を外す指定だが、**WebFetch が外れる**。SKILL.md の手順 3〜4（検索して候補ページを読む）が WebFetch 無しでは成立しない |
| `--dangerously-skip-permissions` で権限判定自体を外す | 書き込みとシェルを許可しないという 5.4 の要求の真逆 |
| `--permission-mode plan` を使う | 計画モードは編集を伴う作業の下見用で、Read / WebSearch / WebFetch だけを使う今回の用途には合わない。権限の絞り込みを**モードの副作用**に頼る形になり、意図が読めなくなる |
| `--permission-prompts host` のまま、アプリ側に許可 UI を作る | 利用者に「このツールを許可しますか」を出すことになる。解析中は進捗表示とキャンセルだけを見せたい（F-25）。**許可を聞かれる設計そのものが要らない** |
| `--add-dir` で `analyzer/` の原本を参照させる（コピーしない） | Read の到達範囲がアプリのインストール先まで広がる。セッションごとにコピーする方（F-23 の「コピーし（またはリンクし）」）が範囲を閉じられる |
| `--max-turns` を使わない | ループ的な試行で利用枠を使い切るのを止める手段が無くなる。5.4 が設定可能にすることを要求している |
| `--bare` で設定・フックを全て無効にする | 利用者の認証（OAuth / キーチェーン）が読まれなくなり、`ANTHROPIC_API_KEY` が必須になる。2 章の「利用者はそれぞれ自分の Claude Code を入れ、自分のアカウントでログインして使う」と噛み合わない |
