# ADR-0019. Web Detection を REST で直接呼び、クライアントライブラリを足さない

- 状態: **採用**（2026-11-14 承認）
- 日付: 2026-11-14

## 背景

F-14 / 5.6 は Google Cloud Vision API の Web Detection を**上級者向けの任意機能**として求めている。
要件が指定しているのは次の 4 点だけで、**呼び方は指定されていない**。

- 認証は**利用者自身の API キー**
- 送るのは**フレーム全体**。顔の検出・切り出し・人物の特定を目的とした使い方はしない
- 使う結果は `bestGuessLabels` / `webEntities` / `pagesWithMatchingImages`（ページのタイトルと URL）
- 結果は `web_detection.json` として一時作業フォルダに置く

決めるべきことは次の 3 つ。

1. 公式のクライアントライブラリ（`Google.Cloud.Vision.V1`）を使うか、REST を直接叩くか
2. 1 回の解析で何回 HTTP を往復するか
3. 応答のどこを拾い、どこを捨てるか

1 が効いてくる。**`Google.Cloud.Vision.V1` は gRPC 経由で、認証は Application Default
Credentials（サービスアカウントの JSON ファイル、または `gcloud` のログイン）を前提にしている。**
API キー 1 本で使う作りになっていない。
サービスアカウントの JSON をファイルで持つことになれば、
5.6 の「API キーはファイルに平文で保存しない」と噛み合わなくなる（鍵の形が変わるだけで、
利用者の PC に平文の資格情報が置かれることは同じ）。

## 決定

### 1. `images:annotate` の REST 端点を `HttpClient` で直接呼ぶ

```
POST https://vision.googleapis.com/v1/images:annotate?key=<APIキー>
Content-Type: application/json
```

新しい NuGet を足さない。`HttpClient` と `System.Text.Json` は標準にある。
**依存が増えないので、D-06（ライセンスの確認）の対象も増えない。**

実装は `NutLens.Analysis`（`net10.0`）に置く。
ADR-0011 で「外部へ出る経路はここだけに置く」と決め、
`.csproj` のコメントにも「段階 3 の後に足す Web Detection（HTTP）もここに入れる」と書いてある。
**外へ出る経路が増えたかどうかを、プロジェクト参照のグラフで見えるようにしておく**（D-08）。

### 2. 要求する機能は `WEB_DETECTION` ただ 1 つ

```json
{
  "requests": [
    {
      "image": { "content": "<JPEG の base64>" },
      "features": [ { "type": "WEB_DETECTION", "maxResults": 10 } ]
    }
  ]
}
```

**`FACE_DETECTION` を入れない。** 顔の検出そのものであり、要件が禁じている。
Celebrity Recognition（`celebrity_recognition`）は別の端点なので、そもそも呼ばない。

Vision API の課金は「画像 × 機能」で数えるため、機能を 1 つに絞ることは費用にも直接効く。

### 3. 3 枚を 1 リクエストにまとめる

`requests` は配列で、1 回の呼び出しに複数の画像を載せられる。
送る枚数（既定 3 枚。ADR-0022）を**配列に並べて 1 往復で送る**。

応答の `responses` は要求と同じ順で返り、画像ごとに `error` を持てるので、
1 枚だけ失敗した場合も枚数単位で切り分けられる。

### 4. 要求も応答も型付きの `record` で扱う

`System.Text.Json` のソース生成（`JsonSerializerContext`）を使う。
`JsonDocument` のまま持ち回らない（CLAUDE.md §4.1：`object` で受け流さない）。

```csharp
internal sealed record VisionWebEntity(string? EntityId, string? Description, double? Score);
internal sealed record VisionPageMatch(string? Url, string? PageTitle);
internal sealed record VisionBestGuessLabel(string? Label, string? LanguageCode);
```

**応答の形が変わったときに、ソース生成の逆直列化が失敗として表れる。**
`JsonDocument` を手で歩くと、同じ変更が「画面に何も出ない」形で表れ、原因に辿り着けない。

### 5. 拾うのは 3 つだけ。画像 URL の羅列は捨てる

| 応答の項目 | 扱い |
|---|---|
| `bestGuessLabels` | 使う（5.6） |
| `webEntities` | 使う（5.6）。`description` が空の項目は捨てる |
| `pagesWithMatchingImages` | 使う（5.6）。`url` と `pageTitle` だけ |
| `fullMatchingImages` / `partialMatchingImages` / `visuallySimilarImages` | **捨てる** |
| `visuallySimilarImages` 内の URL | **捨てる** |

捨てる側は画像ファイルの URL が数十件並ぶだけで、作品名には繋がらない。
これをプロンプトに載せると、**Claude の利用枠を参考情報の残骸で消費する**。

## 影響

- 新しい NuGet は増えない
- JPEG を base64 にして送る。実測で 1 枚あたり約 103 KB
  （利用者の 65 枚の撮影が 6.7 MB）なので、3 枚で約 310 KB、base64 で約 410 KB。
  Vision API の 1 リクエストの上限（20 MB）には十分収まる
- `HttpClient` は `IHttpClientFactory` を使わず、`NutLens.Analysis` 内で 1 つを使い回す。
  呼ぶ先が 1 つしかなく、`Microsoft.Extensions.Http` を足す理由がない
- 端点の URL は定数にする。利用者が変えられるようにはしない
  （送り先を設定で変えられると、D-08 の「外部送信は 2 経路だけ」が設定ファイルで崩れる）

## 実装で分かったこと（2026-11-14）

### 1. 新しい NuGet は本当に 1 つも増えなかった

`System.Net.Http.Json` は共有フレームワークに入っているので、
`PostAsJsonAsync(url, value, JsonTypeInfo, token)` がそのまま使える。
`Directory.Packages.props` に行を足していない（D-06 の確認対象が増えない）。

### 2. 打ち切りに `HttpClient.Timeout` を使わない

使うと、**利用者のキャンセルと時間切れが同じ例外で来て区別できない。**
自前の `CancellationTokenSource` を利用者の token と束ねて、
`cancellationToken.IsCancellationRequested` で見分けている。

- 利用者が止めた → `OperationCanceledException` をそのまま上へ（失敗ではない）
- 時間切れ → `Network` として顛末を返す

### 3. 画像ごとのエラーは全体の失敗として扱う

応答は画像ごとに `error` を持てるが、「3 枚中 1 枚だけ認証エラー」は起こらない。
枚数単位で分けても使い道がないので、1 枚でもエラーが入っていれば全体を失敗にした。

## 宿題

- **実際に呼んで確かめていない。** 利用者の Google Cloud の API キーと課金が要るため、
  実機での確認は利用者の判断を待つ
- 課金の単価（Web Detection が 1000 ユニットあたりいくらか、無償枠が月何ユニットか）を
  **公開されている価格表で確かめて README に書く。** 推測で数字を書かない
- `maxResults` を 10 にした根拠は無い。実物の応答を見て決め直す
- 応答に `error.status = RESOURCE_EXHAUSTED` が入るのか、HTTP 429 で返るのかを確かめる
  （ADR-0022 決定 5 のメッセージ分けがこれに依存する）

## 却下した案

| 案 | 却下の理由 |
|---|---|
| `Google.Cloud.Vision.V1` を使う | gRPC と `Grpc.Core` の依存が増える。**認証が API キーではなくサービスアカウントの JSON ファイル前提**で、利用者の PC に平文の資格情報を置くことになる。5.6 の「平文で保存しない」と噛み合わない（決定 1） |
| 機能を `LABEL_DETECTION` などと併せて要求する | 課金が機能の数だけ増える。**`FACE_DETECTION` は顔の検出そのもので、要件が明示的に禁じている**（決定 2） |
| 画像 1 枚ごとに 1 リクエスト送る | 3 往復になる。1 枚目で認証エラーが出ても残り 2 枚を送ってしまい、**失敗の回数だけ課金される**（決定 3） |
| 画像を Cloud Storage に上げて URL で渡す | 先に Google へアップロードする経路が増える。**外部へ出る経路を 1 つに保てなくなる**（D-08） |
| 応答を `JsonDocument` のまま読む | 型が無いので、Google 側の書式変更が「画面に何も出ない」形で表れる（決定 4） |
| 応答をそのまま `web_detection.json` に書く | 使わない画像 URL が数十件並ぶ。ADR-0022 決定 4 で書式を自前にする |
| 端点の URL を設定で変えられるようにする | 送り先を設定ファイルで差し替えられると、**「外部送信は Claude Code と Web Detection だけ」が設定の書き換えで崩れる**（D-08） |
