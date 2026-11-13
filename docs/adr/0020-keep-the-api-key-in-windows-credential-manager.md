# ADR-0020. API キーを Windows 資格情報マネージャーに預け、設定ファイルにもログにも出さない

- 状態: **採用**（2026-11-14 承認）
- 日付: 2026-11-14

## 背景

5.6 はこう書いている。

> API キーはファイルに平文で保存しない。Windows の資格情報マネージャーまたは DPAPI で保護して保存する。**ログにも出さない**。

**どちらでもよい、と書かれている。**だから選ぶ根拠が要る。

いまの設定は `%LOCALAPPDATA%\NutLens\settings.json` にある。
このファイルは利用者が開いて書き換える前提で作ってある
（`NutLensSettings.Normalize()` に「設定ファイルは利用者が手で書き換えられる」と書いてある）。
**不具合報告に貼られることを前提にすべきファイル**であり、ここに API キーを置く選択肢は無い。

残る論点は 2 つ。

1. 資格情報マネージャーと DPAPI のどちらにするか
2. 「ログにも出さない」をどう保証するか（`claude.log` / `web_detection.json` / 例外メッセージ / 設定画面）

## 決定

### 1. Windows 資格情報マネージャー（`CredWriteW` / `CredReadW` / `CredDeleteW`）に預ける

| 項目 | 値 |
|---|---|
| `TargetName` | `NutLens/GoogleCloudVision` |
| `Type` | `CRED_TYPE_GENERIC` |
| `Persist` | `CRED_PERSIST_LOCAL_MACHINE`（ログオン利用者に紐づく。ローミングしない） |
| `CredentialBlob` | API キーの UTF-16 バイト列 |

DPAPI ではなく資格情報マネージャーにする理由は、暗号の強さではない。
**利用者が自分で見つけて消せるから。**

- 「コントロール パネル → 資格情報マネージャー → Windows 資格情報」に `NutLens/GoogleCloudVision` として並ぶ
- アプリをアンインストールした後でも、利用者は自分の手で消せる

DPAPI で暗号化したバイト列をファイルに置くと、**その所在を知る手段がアプリ側にしかない。**
アンインストール後にファイルが残ると、利用者は消し方を知らないまま資格情報を抱え続ける。
プライバシーを主題にしたアプリ（5.5）で、**自分が置いたものを利用者が消せない状態**を作らない。

### 2. P/Invoke は CsWin32 に生成させる

`NutLens.Capture/NativeMethods.txt` に次を足す（CLAUDE.md §4.4：手書きしない）。

```
CredWriteW
CredReadW
CredDeleteW
CredFree
CREDENTIALW
CRED_TYPE
CRED_PERSIST
```

### 3. 抽象は Core、実装は `NutLens.Capture`

```csharp
// NutLens.Core/Security/ISecretStore.cs
public interface ISecretStore
{
    string? TryRead(string name);
    void Write(string name, string secret);
    void Delete(string name);
}
```

実装 `CredentialManagerSecretStore` は `NutLens.Capture`（`net10.0-windows10.0.22621.0`）に置く。
**`NutLens.Capture` は「撮影の層」ではなく「Windows 専用の実装を置く層」**である（ADR-0002）。
資格情報マネージャーは Windows 専用の API なので、ここ以外に置けない
（`NutLens.Analysis` は `net10.0` で、Windows 専用 API を書くとビルドが落ちる）。

**Web Detection のクライアント（`NutLens.Analysis`）は鍵の出どころを知らない。**
`ISecretStore` から読んだ文字列を引数で受け取るだけにする。

### 4. 鍵を持ち回さない。4 か所に出さないことを名指しで決める

| 出さない先 | どう守るか |
|---|---|
| `settings.json` | `WebDetectionSettings` に鍵のプロパティを**作らない** |
| `claude.log` | 鍵は `NutLens.Analysis` の HTTP 側にしか渡らない。claude の起動引数には現れない |
| `web_detection.json` | 書式を自前で決める（ADR-0022 決定 4）。要求の中身を書かない |
| 例外・画面のメッセージ | 端点の URL を例外に載せない。**URL にはクエリ文字列として鍵が入っている** |

最後の 1 つが落ちやすい。`HttpRequestException.Message` や
`HttpClient` の既定の例外文には要求 URI が入ることがあり、
**`?key=…` ごと画面やログに出る。**
だから「HTTP の失敗は、こちらで作った短い文へ必ず置き換えてから外へ出す」ことを決める。
例外オブジェクトをそのまま上へ投げない。

### 5. 鍵を消す口をアプリに置く

設定画面（段階 4）に「API キーを削除する」を置く。
段階 3' の時点では、同意ダイアログ（ADR-0021）に「キーを消す」を併置する。

**入れる口だけ作って消す口を作らない、をしない。**

## 影響

- `NutLens.Capture` に `Security/` が増える。撮影と無関係のクラスが同じプロジェクトに入る
- 資格情報マネージャーは**ログオン利用者ごと**なので、同じ PC の別利用者には見えない。これは望ましい
- ドメイン参加 PC の資格情報ローミングの対象にしないため `CRED_PERSIST_ENTERPRISE` は使わない
- `ISecretStore` は Web Detection 専用にしない。段階 4 以降で別の秘密が出たときに使い回せる
- README（段階 5）に「API キーの保存先と、自分で消す手順」を書く（D-07）

## 実装で分かったこと（2026-11-14）

### 1. CsWin32 は末尾の `W` を落とした名前で生成する

`NativeMethods.txt` には `CredWriteW` / `CredReadW` / `CredDeleteW` と書くが、
生成されるのは `PInvoke.CredWrite` / `CredRead` / `CredDelete`。
`CredFree` には `W` が無いのでそのまま。**書く名前と呼ぶ名前が違う。**

### 2. 往復は確かめた。`CredentialBlobSize` はバイト長で正しい

単体テストでは守れない（`CredWriteW` は Windows 専用 API で、
テストは `net10.0` なので `NutLens.Capture` を参照できない）。
そこで本物の鍵に触らない自己診断を作った。

```powershell
dotnet run --project tools/NutLens.CaptureProbe -- --secret-selftest
```

実測（`NutLens/SelfTest` という別の名前で書いて読んで消す）:

```
書く値   : 26 文字          ← 日本語を混ぜてある（UTF-16 でバイト長が文字数の 2 倍）
読めた値 : 26 文字
往復     : 一致（末尾が切れていない）
削除     : 消えた
```

**鍵まわりを触ったら、必ずこれを走らせる。**

### 3. 鍵が無い状態の読み出しも確かめた

`--env` に Web Detection の節を足し、3 条件の揃い具合を出すようにした。

```
設定で有効               : いいえ（既定）
同意の版                 : 0（今の版 1）
API キー                 : なし
送る枚数                 : 3
判定                     : 送られない（Disabled）
```

**鍵が無いときに例外にならず null が返る**ことを、ここで確かめている（決定 3）。

## 宿題

- 資格情報マネージャーの一覧に、意図した名前で並ぶかを**目視する（未了）**
- **本物の API キーでの読み書きは未検証**（自己診断は別名の疑似値で行っている）
- アンインストール時に資格情報を消すかどうか（段階 5 の D-01 と合わせて決める）

## 却下した案

| 案 | 却下の理由 |
|---|---|
| `settings.json` に平文で書く | 5.6 が禁じている。settings.json は**利用者が開き、不具合報告に貼る前提**のファイル |
| DPAPI（`System.Security.Cryptography.ProtectedData`）で暗号化したファイルを置く | 暗号化はされるが、**その所在を利用者が知る手段がアプリにしかない。** アンインストール後に残ると消し方が無くなる。NuGet も 1 つ増える（決定 1） |
| 環境変数（`GOOGLE_API_KEY` など）に置かせる | 環境変数は**子プロセスに丸ごと受け継がれる。** 解析のために起動する `claude` に API キーが渡ることになり、送り先を絞る D-08 の趣旨に反する |
| 解析のたびに入力させる | 保存の問題は消えるが、解析のたびに貼り付けることになる。**貼り付けた文字列がクリップボードに残る**という別の経路も増える |
| `ISecretStore` を作らず、`NutLens.Analysis` から直接 `CredReadW` を呼ぶ | `NutLens.Analysis` は `net10.0`。Windows 専用 API を書くとビルドが落ちる（ADR-0002 の仕掛けが効く） |
| 例外をそのまま上の層へ投げる | `HttpRequestException` の文面に要求 URI が入ることがあり、**`?key=…` ごとログと画面に出る**（決定 4） |
