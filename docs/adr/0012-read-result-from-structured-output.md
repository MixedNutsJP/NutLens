# ADR-0012. 結果は structured_output から読み、result 文字列を予備にする

- 状態: **実測・実機で確認**（2026-11-13。通しで `claude -p` を走らせて結果を読めることを確認）
- 日付: 2026-11-13

## 背景

F-24 は「`--output-format json` と `--json-schema` を使い、結果を受け取ってパースする」。
受け取る側を書くには、実際にどういう形で返ってくるかを知る必要がある。

実測（Claude Code 2.1.322、2026-11-13）。スキーマは
`{"type":"object","properties":{"answer":{"type":"string"},"confidence":{"type":"number"}},…}`。

```json
{
  "type": "result",
  "subtype": "success",
  "is_error": false,
  "session_id": "7ac8f6e5-1284-4dd6-8a27-414d1bd94ccc",
  "num_turns": 2,
  "stop_reason": "tool_use",
  "terminal_reason": "completed",
  "total_cost_usd": 0.0479236,
  "permission_denials": [],
  "result": "{\"answer\":\"2\",\"confidence\":1}",
  "structured_output": { "answer": "2", "confidence": 1 },
  "credential_expires_at": "2026-11-18T15:07:03.906Z"
}
```

**同じ内容が 2 か所にある。** `structured_output` はオブジェクト、`result` は同じものの文字列。

## 決定

### 1. `structured_output` を第一とし、`result` を予備にする

```
1. 標準出力を JSON として読む
2. structured_output があれば、それを AnalysisResult へ逆直列化する
3. 無ければ result（文字列）を JSON として読み直して逆直列化する
4. どちらも駄目なら InvalidAnalysisResultException
```

`result` を文字列として二重に読み直す経路を予備に残すのは、
**どちらか片方が将来の版で消えても解析が止まらないようにする**ため。

### 2. 失敗の判定は `is_error` と `subtype` と終了コードの 3 つで行う

| 見るもの | 失敗の意味 |
|---|---|
| プロセスの終了コード ≠ 0 | 起動や認証の失敗 |
| `is_error` が true | Claude 側がエラーで終わった |
| `subtype` が `"success"` 以外 | 同上（`subtype` は "success" / "error_max_turns" などを取る） |

どれか 1 つでも失敗なら、**生の標準出力を添えて**利用者に出す。
「JSON が壊れている」とだけ言われても利用者は何もできない（非機能要件）。

### 3. 失敗しても捨てずに残す情報

| フィールド | 使い道 |
|---|---|
| `session_id` | 自前の ID と照合してログに残す。食い違ったら**出力側を正とする**（ADR-0007 決定 2） |
| `num_turns` | `--max-turns` に当たったかの目安。結果画面に出す |
| `total_cost_usd` | 結果画面に出す。D-07「自分の利用枠を使う」ことを実感できるようにする |
| `permission_denials` | 空でなければ `claude.log` に残す。絞り込み（ADR-0006）が意図どおりかの点検になる |
| `credential_expires_at` | 期限が近ければ初回セットアップの案内へ誘導する（段階 5） |

### 4. `AnalysisResult` は型付きの `record` にする

`object` や `dynamic` で受け流さない（ADR-0010）。
スキーマに無いフィールドが来ても落ちないよう、未知のプロパティは無視する。

### 5. パーサは `NutLens.Core` に置く

文字列から `record` を作るだけで外界に触らない。単体テストの対象にする。

## 実装で分かったこと（2026-11-13）

### 1. `$schema` を書くとスキーマごと拒否される

`result.schema.json` の先頭に慣例どおり `$schema` を書いていたら、起動直後に落ちた。

```
Error: --json-schema is not a valid JSON Schema:
  no schema with key or ref "https://json-schema.org/draft/2020-12/schema"
```

Claude Code 側の検証器がこのメタスキーマを知らない。**`$schema` の行を消したら通った。**
スキーマ自体の意味は変わらないので、消して問題ない。

**落ちるのは `claude -p` の起動直後**で、標準出力は空になる。
標準エラーを `claude.log` に残していなければ、理由が分からないまま
「Claude Code が異常終了しました」とだけ出ていた。決定 2 の方針が実際に効いた場面。

### 2. 実測した出力の読み取り（通し確認）

```
成否             : 成功
ターン数         : 5
所要             : 12.9 秒
費用             : $0.1183
停止理由         : tool_use
拒否されたツール : 0 件
セッション照合   : 一致
候補             : 0 件
```

- `structured_output` から型付きの `record` へ逆直列化できた
- **候補 0 件が「成功」として扱われた。** 入力が映画ではないので正しい答えで、
  `analyzer/CLAUDE.md` の「無理に 1 つ挙げない」が効いている
- `permission_denials` は 0 件。ツールの絞り込み（ADR-0006）が邪魔をしていない

### 3. ログは UTF-8（BOM つき）で書く

BOM 無しで書いたら、Windows の既定のコードページで開かれて日本語が化けた。
`claude.log` と `prompt.md` は利用者が開いて読むものなので、BOM を付ける。

## 影響

- `claude.log` には**標準出力をそのまま**書く。パース前の生の形が残っていないと、
  不具合報告から原因をたどれない
- `structured_output` を使うので、スキーマ違反の検出は Claude Code 側に任せることになる。
  こちらでもスキーマ検証をするかは、必要になってから決める（今は過剰）

## 却下した案

| 案 | 却下の理由 |
|---|---|
| `result` 文字列だけを読む | 二重に JSON を読むことになり、**エスケープの扱いを一段多く間違えられる**。オブジェクトが直接あるのに使わない理由がない |
| `structured_output` だけを読む | 片方しか見ない実装は、そのフィールドが消えた版で全面的に止まる。予備があれば止まらない |
| `--output-format stream-json` を使う | 進捗（F-25）を細かく出せる利点はあるが、1 行ごとに異なる型の JSON を読む実装が要る。段階 2 では「動いている／キャンセルできる」で足り、進捗の粒度は要件にない |
| 終了コードだけで成否を判定する | `is_error: true` でも終了コードが 0 になる場合がありうる（確かめていない）。**確かめていないものに賭けない** |
| `is_error` だけで判定する | 起動自体に失敗したとき（claude が無い、認証切れ）は JSON が返らない。終了コードが要る |
| 結果を `JsonElement` のまま画面へ渡す | 画面側が JSON の形を知ることになり、スキーマを変えたときの影響範囲が広がる。型付きの `record` で受ける（ADR-0010） |
