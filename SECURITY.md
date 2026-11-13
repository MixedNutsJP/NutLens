# セキュリティ / Security

## 脆弱性の報告 / Reporting a vulnerability

**公開の Issue に書かないでください。**
**Please do not open a public Issue.**

GitHub の [Security Advisories](https://github.com/MixedNutsJP/NutLens/security/advisories/new)
から非公開で報告してください。

Report it privately through
[Security Advisories](https://github.com/MixedNutsJP/NutLens/security/advisories/new).

**個人で作っているソフトウェアです。** 返信までに時間がかかることがあります。
**This is made by one person**, so a reply may take a while.

## 報告のときに添えないでほしいもの / Please do not attach

- **撮影した画像・音声**（一時データとして消える前提のものです）
- **API キー**（Google Cloud Vision のもの。資格情報マネージャーにあります）
- **`claude.log`**（プロンプトの内容が入っています）

- **Captured images or audio** (they are meant to be deleted)
- **Your API key** (the Google Cloud Vision one, kept in Credential Manager)
- **`claude.log`** (it contains the prompt)

代わりに、**何をしようとして何が起きたか**を書いてください。
Describe **what you tried and what happened** instead.

## このソフトウェアが外部とやりとりするもの / What talks to the outside

**把握しておいていただきたい範囲です。**
**Worth knowing before you audit it.**

| 相手 / Party | 向き / Direction | いつ / When | 内容 / What |
|---|---|---|---|
| **Claude Code**（利用者の PC 上） | 送信 / out | 解析のとき / On analysis | フレーム画像・文字起こし・OCR・メモ。**そこから先は Claude Code の責任範囲** / Frames, transcript, OCR, notes |
| **GitHub Releases** | 受信 / in | 起動時の更新確認（切れます） / Update check at startup (can be turned off) | 公開されている版の一覧。**送るデータはありません** / The list of releases. **Nothing is sent** |
| **Hugging Face** | 受信 / in | 利用者がモデルを落としたとき / When the user downloads a model | ggml モデル。**送るデータはありません** / The model file. **Nothing is sent** |
| **Google Cloud Vision** | 送信 / out | **既定オフ。**同意し、API キーを入れたときだけ / **Off by default**; only after consent and an API key | 選んだフレーム（既定 3 枚まで） / The selected frames |

**アプリ独自の送信（テレメトリ）はありません。**
**There is no telemetry.**

### 通信するコードの場所 / Where the networking code lives

**`src/NutLens.Analysis/` だけです。** ほかのプロジェクトは外へ出ません
（[ADR-0011](docs/adr/0011-isolate-claude-invocation-in-analysis-project.md)）。
更新の確認だけは `src/NutLens.App/Services/VelopackUpdateService.cs` にあります。

**Only `src/NutLens.Analysis/`.** The update check is the one exception, in
`src/NutLens.App/Services/VelopackUpdateService.cs`.

**送り先の URL はすべて定数**で、設定ファイルからは変えられません。
**All endpoints are constants** and cannot be changed from the settings file.

## 秘密情報の扱い / Handling of secrets

| 種類 / Kind | 置き場 / Where |
|---|---|
| Google Cloud Vision の API キー | **Windows 資格情報マネージャー**（`NutLens/GoogleCloudVision`）。設定ファイルにもログにも書きません / **Windows Credential Manager**; never in the settings file or logs |
| Claude の資格情報 | **NutLens は触りません。** Claude Code が自分で持っています / **NutLens never touches these** |

詳しくは [ADR-0020](docs/adr/0020-keep-the-api-key-in-windows-credential-manager.md) を参照してください。
See [ADR-0020](docs/adr/0020-keep-the-api-key-in-windows-credential-manager.md) for details.

## 配布物の署名 / Signing

**署名していません。** 理由と、代わりに確かめられることは
[CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) にあります。

**Releases are not signed.** See [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md)
for why, and for what you can verify instead.

## 対応している版 / Supported versions

**最新の版だけです。** 古い版への修正の配布はしません。
**Only the latest release.** Fixes are not backported.
