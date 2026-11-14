# NutLens

**ショート動画で紹介されている映画・ドラマ・短編を、画面と音から特定する Windows アプリです。**
再生中の画面をキャプチャして手元で下ごしらえし、**利用者自身の Claude Code** に渡して調べさせます。

**A Windows app that works out which film, drama or short a video is introducing, from the
screen and the sound.** It captures the screen while you watch, prepares the material locally,
and hands it to **your own Claude Code** to investigate.

> **動画のダウンロードはしません。** 再生中の画面と音を記録するだけです。
> **動画の取り扱いは、各サービスの規約に従ってください。**
>
> **No video is downloaded.** It only records the screen and sound while you play it.
> **Please follow the terms of the service you are watching on.**

> **人物の特定はしません。** 顔の検出・顔の切り出し・人物の照合は実装していません。
> 手がかりに使うのは、あらすじ・舞台・小道具・テロップ・ナレーションといった**人物以外の情報**です。
>
> **It does not identify people.** No face detection, no face cropping, no person matching.
> The clues are **non-person information**: the plot, the setting, props, captions and narration.

## 必要なもの / Requirements

| 項目 / Item | 内容 / Detail |
|---|---|
| OS | Windows 10 バージョン 2004（19041）以降 / Windows 10 version 2004 (19041) or later |
| [Claude Code](https://docs.claude.com/en/docs/claude-code) | **必須。** 利用者自身がインストールし、ログインしておく。解析は**利用者の利用枠**を使います / **Required.** Installed and logged in by you; analysis uses **your own plan** |
| Whisper のモデル | 文字起こしに使う。**同梱しません。** 初回セットアップでダウンロードします / For transcription. **Not bundled**; downloaded at first run |
| 日本語の OCR 言語パック | テロップの読み取りに使う。無くても解析は続きます / For reading captions. Analysis continues without it |

## インストール / Installation

[Releases](https://github.com/MixedNutsJP/NutLens/releases) から
**`NutLens-Setup.exe`** を落として実行します。
**管理者権限は要りません**（利用者ごとのインストールです）。

Download **`NutLens-Setup.exe`** from
[Releases](https://github.com/MixedNutsJP/NutLens/releases) and run it.
**No administrator rights are needed** (it installs per user).

> **いまの版はすべて「Pre-release」です。**
> `0.x` の間は prerelease として出します（[ADR-0039](docs/adr/0039-mark-zero-major-and-suffixed-versions-as-prerelease.md)）。
> **`Latest` の札は付きません。** 一覧の一番上にあるものが最新です。
> 確かめていないことが何かは、下の「動作確認の範囲」を読んでください。
>
> **Every release so far is a pre-release.** While the version is `0.x` it ships as a
> pre-release, so **no `Latest` badge is shown** — the topmost entry is the newest.
> See "What has been checked" below for what is not verified.

インストーラーを使いたくない場合は、同じ Releases の**ポータブル版（zip）**も使えます。
**ポータブル版では自動更新が効きません。**

A **portable zip** is available in the same Releases.
**Automatic updates do not work with the portable version.**

### インストール先を選ぶ / Choosing the install location

**インストーラーの画面で選べます。** 既定は `%LOCALAPPDATA%\NutLensApp\` です。

**You choose it in the installer.** The default is `%LOCALAPPDATA%\NutLensApp\`.

> **NutLens 専用の空のフォルダを選んでください。**
> **アンインストールは、そのフォルダの中身を選別せずに全部削除します。**
> 既存のファイルがあるフォルダを選ぶと、インストーラーが警告します。
>
> **Pick an empty folder used only by NutLens.**
> **Uninstalling deletes everything in that folder**, so the installer warns you
> if the folder you picked is not empty.

**本体をどこに入れても、設定・履歴・Whisper のモデルの置き場は変わりません**
（既定は `%LOCALAPPDATA%\NutLens\`）。インストール先には何も書き込みません。
データの置き場だけを別に移したい場合は、下の「データの保存先」を参照してください。

**Wherever the app goes, the settings, history and Whisper model stay where they are**
(`%LOCALAPPDATA%\NutLens\` by default) — nothing is written into the install directory.
To move the data itself, see "Where the data lives" below.

### 更新 / Updates

起動時に背景で更新を確認し、**見つかったときだけ**尋ねます。
最新のときも、確認できなかったときも、何も出しません。
確認を止めたい場合は、設定画面の「高度な設定（上級者向け）」で切れます。

It checks for updates in the background at startup and asks **only when one is found**.
Nothing is shown when you are up to date, or when the check fails.
You can turn the check off in the advanced settings.

### アンインストール / Uninstalling

Windows の「アプリと機能」から削除します。そのとき——

Remove it from "Apps & features". When you do:

| 消えるもの / Removed | 残るもの / Kept |
|---|---|
| アプリ本体 / The application | **履歴**（`%LOCALAPPDATA%\NutLens\history\`） / **The history** |
| 一時作業フォルダ / The temporary work folder | Whisper のモデル（`models\`） / The Whisper model |
| 設定（`settings.json`） / The settings | 資格情報マネージャーの API キー / The API key in Credential Manager |

**履歴とモデルは黙って消しません。** 利用者の記録と、落とし直すと時間のかかるものだからです。
消す場合は `%LOCALAPPDATA%\NutLens\` を削除してください。
API キーは「コントロール パネル → 資格情報マネージャー → Windows 資格情報」から
`NutLens/GoogleCloudVision` を削除します。

**The history and the model are never deleted silently** — one is your record, the other takes
a long time to download again. To remove them, delete `%LOCALAPPDATA%\NutLens\`.
The API key is removed from Control Panel → Credential Manager → Windows Credentials
(`NutLens/GoogleCloudVision`).

## 使い方 / How to use

1. ブラウザなどでショート動画を再生します。 / Play the short video in your browser.
2. NutLens で**撮影する対象**を選びます（ウィンドウ / モニタ全体 / 画面の一部）。
   Choose **what to capture** (a window, a whole monitor, or part of the screen).
3. 「撮影を開始」またはホットキー（既定 `Ctrl+Shift+F9`）で撮影します。
   Press "撮影を開始" or the hotkey (default `Ctrl+Shift+F9`).
4. 撮影が終わると、重複した場面が除かれ、文字起こしと OCR が行われます。
   When it stops, duplicate scenes are removed and transcription and OCR run.
5. **送る内容を確認・編集**してから Claude Code に渡します。
   **Review and edit what will be sent**, then hand it to Claude Code.
6. 結果（第一候補・他の候補・根拠・参考 URL）が表示されます。外れていれば追加情報を足して再調査できます。
   The result (top candidate, others, evidence, reference URLs) is shown; if it is wrong you can
   add information and investigate again.

> **動画の部分だけを「画面の一部」で選ぶと精度が上がります。** ブラウザの窓ごと撮ると、
> タブやサイドバーなど**動かない部分が画面の大半を占め**、場面の違いが埋もれます。
>
> **Selecting just the video area improves the result.** Capturing the whole browser window
> leaves tabs and sidebars — **the unchanging majority of the picture** — to drown out the
> differences between scenes.

## 撮影したデータの扱い / What happens to the captured data

**最も気をつけているところです。**
**This is the part we are most careful about.**

| 種類 / Kind | 扱い / Handling |
|---|---|
| 画像・音声・文字起こし・OCR・プロンプト・ログ | **一時データ。** 次のいずれかで**完全に削除**します：結果画面を閉じた／次の撮影を始めた／ウィンドウを閉じた／アプリを終了した / **Temporary.** Deleted completely when the result window closes, the next capture starts, the window is closed, or the app exits |
| 結果とメモ | 履歴として残します（`%LOCALAPPDATA%\NutLens\history\`）。**画像のサムネイルは持ちません。** 設定画面から個別・一括で消せます / Kept as history. **No image thumbnails.** Deletable one by one or all at once |
| アプリ独自の送信 | **ありません。** テレメトリは入れていません / **None.** No telemetry |

### 外部とやりとりするもの / What talks to the outside

**撮影したものが出ていくのは 2 つだけです。** ほかは受信（ダウンロード）で、送るデータはありません。

**Only two things send anything out.** The rest are downloads; nothing is sent.

| 相手 / Party | 向き / Direction | 内容 / What |
|---|---|---|
| **Claude Code**（必須 / required） | **送信 / out** | フレーム画像・文字起こし・OCR・メモ / Frames, transcript, OCR, notes |
| **Google Cloud Vision**（任意・**既定オフ** / optional, **off by default**） | **送信 / out** | 選んだフレーム（最大 3 枚） / The selected frames |
| GitHub Releases | 受信 / in | 更新の確認。**切れます** / Update check. **Can be turned off** |
| Hugging Face | 受信 / in | Whisper のモデル（落とすと決めたときだけ） / The Whisper model, only when you ask for it |

**アプリ独自の送信（テレメトリ）はありません。**
**There is no telemetry.**

### Claude Code 側に残るもの / What stays on the Claude Code side

**Claude Code は自身の会話履歴を利用者の PC に保存します。読み取らせた画像も base64 で残ります。**
NutLens からは削除しません（他の会話を巻き込むため）。結果画面に履歴ファイルの場所を表示するので、
消す場合はそのファイルを削除するか、`claude purge <セッションフォルダ>` を使ってください。

**Claude Code keeps its own conversation log on your PC, and the images it was asked to read
remain there as base64.** NutLens does not delete it (that would affect your other
conversations). The result window shows where the file is; delete it yourself, or use
`claude purge <session folder>`.

## Web Detection（上級者向け・任意） / Web Detection (advanced, optional)

フレームを Google Cloud Vision の Web Detection にかけて、似た画像が載っているページを
参考情報として Claude Code に渡す機能です。

An optional feature that runs frames through Google Cloud Vision's Web Detection and passes
pages with matching images to Claude Code as reference material.

> ### ⚠️ 動作確認をしていません / Not verified
>
> **この機能は、実際に Google Cloud Vision API を呼んだ確認を一度も行っていません。**
> 実装とテスト（送信条件の判定・要求の組み立て・応答の読み取り・失敗の切り分け）は
> 入っていますが、**本物の API キーで通信した確認が取れていません。**
> 使う場合はその前提でお願いします。不具合の報告は歓迎します。
>
> **This feature has never been verified against the real Google Cloud Vision API.**
> The implementation and its tests are in place (the send conditions, building the request,
> reading the response, classifying failures), but **no call has ever been made with a real
> API key.** Please use it with that in mind. Reports are welcome.

- **既定はオフです。** 設定画面の「高度な設定（上級者向け）」にあります。
  **Off by default**, under "高度な設定（上級者向け）" (advanced settings).
- 有効にするには**同意ダイアログ**を通る必要があります。
  **設定ファイルを手で書き換えても有効になりません**（API キーが設定ファイルに存在しないため）。
  Enabling it requires going through a **consent dialog**. **Editing the settings file by hand
  does not enable it** (the API key is not stored there).
- **利用者自身の Google Cloud の API キーと課金設定が必要です。** 利用料がかかる場合があります。
  Google Cloud の規約に従ってご利用ください。
  **Your own Google Cloud API key and billing are required**, and charges may apply. Follow
  Google Cloud's terms.
- 送るのは**フレーム全体**（既定 3 枚まで）です。**顔の検出や人物の特定には使いません。**
  要求する機能は `WEB_DETECTION` ひとつだけです。
  **Whole frames** are sent (up to 3 by default). **Not used for face detection or identifying
  people**; the only feature requested is `WEB_DETECTION`.
- API キーは**設定ファイルに書きません。** Windows の資格情報マネージャーに
  `NutLens/GoogleCloudVision` という名前で保存します。ログにも出しません。
  アプリから削除できますし、資格情報マネージャーから直接消すこともできます。
  The API key is **not written to the settings file**; it is stored in Windows Credential
  Manager as `NutLens/GoogleCloudVision` and never written to logs. You can delete it from the
  app, or directly from Credential Manager.

## 設定 / Settings

`%LOCALAPPDATA%\NutLens\settings.json`（設定画面から変更できます / editable from the settings window）

| 項目 / Key | 既定 / Default | 意味 / Meaning |
|---|---|---|
| `capture.intervalSeconds` | 2.0 | 撮影間隔（秒） / Capture interval |
| `capture.durationSeconds` | 60.0 | 撮影時間（秒） / Capture duration |
| `capture.duplicateThreshold` | 12 | 重複とみなす距離（576 bit 中） / Distance treated as a duplicate |
| `capture.maxFrames` | 20 | 保存する最大枚数 / Maximum frames kept |
| `capture.longEdgePixels` | 1280 | 縮小後の長辺（px） / Long edge after downscaling |
| `capture.recordAudio` | true | システム音声を録る / Record system audio |
| `claude.executablePath` | 自動 / auto | `claude` の場所 / Where `claude` is |
| `claude.maxTurns` | 30 | 解析の最大ターン数 / Maximum turns |
| `hotkey.gesture` | `Ctrl+Shift+F9` | 撮影の開始・停止 / Start and stop capture |
| `webDetection.enabled` | **false** | Web Detection（上記） / Web Detection (see above) |
| `keepTemporaryData` | **false** | デバッグ用。**一時データを消さなくなります** / Debug only. **Stops the temporary data from being deleted** |

### データの保存先 / Where the data lives

既定は `%LOCALAPPDATA%\NutLens\` ですが、**設定画面の「高度な設定（上級者向け）」から
別のドライブに移せます。** C ドライブを使いたくない場合に。
Whisper のモデル（最大 1.5 GB）も一緒に移ります。

The default is `%LOCALAPPDATA%\NutLens\`, but you can **move it to another drive from the
advanced settings** — useful if you would rather not use the C: drive. The Whisper model
(up to 1.5 GB) moves with it.

移すときは「いまのデータごと移す／移さずに切り替える」を選べます。
When you change it, you choose whether to bring the existing data along.

> **録音はシステム全体の音を拾います。** 通知音や他のアプリの音も入ります。
> **Recording captures all system sound**, including notifications and other apps.

## 動作確認の範囲 / What has been checked

**正直に書きます。** 確かめていないことを「動く」と書きません。
**Stated plainly.** We do not write "works" for things we have not checked.

| 機能 / Feature | 状態 / State |
|---|---|
| 画面のキャプチャ・重複除去・間引き | **実機で確認**（実物のショート動画 65 枚で測定） / **Checked on real hardware** |
| Claude Code との連携・結果の表示・再調査 | **実機で確認** / **Checked** |
| テロップの OCR | **実機で確認**（実物のフレームで日本語字幕を読めた） / **Checked** |
| 文字起こし | **日英の音声で確認（`base` のみ）。** ループバックで録った音での通し確認は未了。**`small` 以上は一度も動かしていない** / Checked with Japanese and English audio (**`base` only**); larger models **never run** |
| ホットキー | 登録できることは確認。**押して反応するかは未確認** / Registration checked; **not yet checked by actually pressing it** |
| **Web Detection** | **未確認**（上記の節を参照） / **Not verified** (see above) |
| インストーラー（インストール先を画面で選べる） | **実機で確認** / **Checked on real hardware** |
| 初回セットアップ画面 | **実機で確認** / **Checked** |
| **自動更新** | **一度も更新していない** / **No update has ever been applied** |
| アンインストール（履歴とモデルが残る） | **実機で確認** / **Checked** |
| モデルのダウンロード | **実機で確認**（中断と再開は未検証） / **Checked** (resume not tested) |
| CI（ビルドとテスト） | **実機で確認**（初回 push で `build` / `docs` とも成功） / **Checked** (both jobs passed on the first push) |
| リリースの自動化（タグ打ち） | **実機で確認**（v0.1.0 を公開。`vpk` / Inno Setup / ISCC がランナーで動いた） / **Checked** |

**HDCP で保護された再生（一部の配信サービス）では、画面が黒く映ります。** これは Windows の
仕様で、NutLens では回避できません。
**With HDCP-protected playback (some streaming services) the picture comes out black.** That is
how Windows works; NutLens cannot work around it.

## ビルド / Build

.NET 10 SDK が必要です。 / Requires the .NET 10 SDK.

```
git clone https://github.com/MixedNutsJP/NutLens.git
cd NutLens
dotnet build NutLens.slnx -c Debug
dotnet test
dotnet run --project src/NutLens.App
```

画面を触らずに各機能を確かめるツールもあります（製品には含めません）。
There is also a probe tool for checking things without the GUI (not part of the product):

```
dotnet run --project tools/NutLens.CaptureProbe -- --env              使えるもの一覧 / what is available
dotnet run --project tools/NutLens.CaptureProbe -- --hotkey-selftest  ホットキーの登録 / hotkey registration
dotnet run --project tools/NutLens.CaptureProbe -- --secret-selftest  資格情報の往復 / credential round-trip
dotnet run --project tools/NutLens.CaptureProbe -- 3 --audio-idle     録音（音は出ません） / recording (silent)
```

## 配布とライセンス / Distribution and licensing

| 文書 / Document | 中身 / Contents |
|---|---|
| [LICENSE](LICENSE) | MIT |
| [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) | 使用しているライブラリ（**すべて MIT**） / Libraries used (**all MIT**) |
| [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) | **署名していない**こととその理由 / Why releases are **not signed** |
| [SECURITY.md](SECURITY.md) | 脆弱性の報告・**外部とやりとりするものの一覧** / Reporting, and **everything that talks to the outside** |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 開発に参加する場合の作法 / How to contribute |

## 設計資料 / Design documents

決定の記録（ADR）と設計書をリポジトリに入れています。**なぜそう作ったかと、何を確かめていないかが
書いてあります。**
The decision records (ADRs) and design documents are in the repository. **They say why it is
built this way, and what has not been checked.**

- [docs/adr/README.md](docs/adr/README.md) — 決定の一覧と**未処理の宿題** / Decisions and **open questions**
- [docs/design/00-traceability.md](docs/design/00-traceability.md) — 要件と実装の対応表 / Requirements to code
- [docs/design/01-overview.md](docs/design/01-overview.md) — 概要設計 / Overview

## 免責事項 / Disclaimer

**このソフトウェアは無保証で提供されます。使用によって生じたいかなる損害についても、
作者は一切の責任を負いません。**

**This software is provided as-is, without any warranty. The author accepts no liability
for any damage arising from its use.**

**解析の結果は推測です。** 外れることがあります。第一候補が正しいとは限りません。
**The result is a guess** and can be wrong. The top candidate is not necessarily correct.

**動画の取り扱いは、各サービスの規約に従ってください。** 本ソフトウェアは、
視聴中の画面をキャプチャするだけのものです。
**Follow the terms of the service you are watching on.** This software only captures the
screen while you watch.

**撮影した画像・音声の権利は、その作品の権利者にあります。**
このソフトウェアは、**自分が何を見たのかを自分で調べるため**のものです。
撮影したものを公開・配布・再利用する用途は想定していません。
お住まいの地域の法に従ってお使いください。

**The frames and audio you capture belong to the rights holders of the work.**
This software exists so that **you can look up what you just watched** — it is not intended
for publishing, redistributing or reusing what you captured. Follow the law where you live.

## 不具合の報告 / Reporting issues

GitHub の Issue でご報告ください。
Please open a GitHub Issue.

**撮影した画像や音声は添付しないでください。** 一時データとして消える前提のものです。
代わりに、何をしようとして何が起きたか、`settings.json` の内容（**API キーは含まれません**）、
エラーメッセージを添えてください。

**Please do not attach captured images or audio** — they are meant to be deleted. Instead,
describe what you tried and what happened, and include your `settings.json` (**it never
contains the API key**) and the error message.

---

## クレジット / Credits

**Created by MixedNuts**

## ライセンス / License

MIT License — 詳細は [LICENSE](LICENSE) を参照してください。
See [LICENSE](LICENSE) for details.

再配布・改変は自由ですが、**著作権表示とライセンス文を必ず残してください。**
MIT ライセンスの条件です。

You are free to redistribute and modify this, but **the copyright notice and the
license text must be retained** — that is a condition of the MIT License.

使用しているライブラリとそのライセンスは [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
にまとめています（アプリの「このアプリについて」からも開けます）。
The libraries used and their licenses are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) (also reachable from "このアプリについて"
in the app).
