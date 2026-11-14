# 要件トレーサビリティ表

- 最終更新：2026-11-14（段階 5 実装時点）
- 出典：`implementation-prompt.md`
- **要件の実現箇所が変わったら、コードと同じ作業の中でこの表を更新する**（`CLAUDE.md`）

状態の記号：

| 記号 | 意味 |
|---|---|
| ✅ | 実装済み・テスト済み |
| 🔨 | 実装中 |
| 📋 | 設計済み・未実装 |
| — | 着手前 |

---

## 5.1 キャプチャ

| ID | 要件 | 設計 | 実現クラス | テスト | 段階 | 状態 |
|---|---|---|---|---|---|---|
| F-01 | 対象をウィンドウ／画面の矩形で選べる。WGC を使う | 02 §9 / ADR-0003 | `WindowCaptureTargetProvider` `MonitorCaptureTargetProvider` `CaptureInterop` `WgcFrameSource` `CroppingFrameSource` `OverlayRegionSelector` | `CroppingFrameSourceTests` ＋ 実機（CaptureProbe） | 1 | ✅ |
| F-02 | 撮影間隔 0.5〜10 秒（既定 2） | 02 §7 | `CaptureSettings.IntervalSeconds` `WgcFrameSource` | `SettingsTests` `CaptureSessionServiceTests` | 1 | ✅ |
| F-03 | 撮影時間を指定（既定 60 秒）／手動停止 | 02 §8.1 | `CaptureSettings.DurationSeconds` `CaptureSessionService` | `CaptureSessionServiceTests` | 1 | ✅ |
| F-04 | グローバルホットキーで開始・停止（キー変更可） | 06 §3 / ADR-0023 | `HotkeyGesture` `HotkeyService` | `HotkeyGestureTests`（12 件）＋ `--hotkey-selftest` で登録を実測 | 4 | 🔨（**押して届くかは未検証**） |
| F-05 | 知覚ハッシュで重複除去。しきい値を設定可 | 02 §4 / ADR-0004 / **ADR-0015** | `DHash`（25×24・576 bit） `PerceptualHash` `DHashDuplicateFrameFilter` | `DHashTests` `DuplicateFrameFilterTests` ＋ 実物のフリップ動画 14 / 35 / 65 枚で実測 | 1 | ✅（ループ込みで検証。**繰り返しは一部しか落ちない**＝02 §4.5） |
| F-06 | 長辺 1280px へ縮小（設定可）／上限 20 枚を超えたら時間的に均等に間引く | 02 §5 / 02 §10 / ADR-0008 | `WicFrameEncoder` `EvenTimeFrameThinner` | `EvenTimeFrameThinnerTests` ＋ 実機（2560×1440 → 1280×720） | 1 | ✅ |
| F-07 | 撮影ごとに一時作業フォルダを作る | 02 §6 | `SessionLayout` `FileSystemSessionWorkspace` | `SessionLayoutTests` `FileSystemSessionWorkspaceTests` | 1 | ✅ |

## 5.2 補助情報

| ID | 要件 | 設計 | 実現クラス | テスト | 段階 | 状態 |
|---|---|---|---|---|---|---|
| F-11 | WASAPI ループバック録音＋ローカル文字起こし（日英・自動判定） | 04 §3〜4 / ADR-0016 / ADR-0017 | `LoopbackAudioRecorder` `WhisperTranscriber` `MediaProcessingService` | `CaptureSessionServiceTests`（録音の開始・停止・失敗）`MediaProcessingServiceTests` ＋ **日英の文字起こしを実測** | 3 | 🔨（文字起こしは確認済み。**ループバックで録った音での通し確認が未了**） |
| F-12 | 残フレームに OCR をかけ `ocr.txt` にまとめる（撮影時刻つき・重複除去） | 04 §5 / ADR-0018 | `WindowsOcrEngine` `OcrTextCollector` `MediaTextFormatter` | `OcrTextCollectorTests` `MediaTextFormatterTests` ＋ **実物 65 枚で実測** | 3 | ✅ |
| F-13 | URL・投稿者名・キャプション・自由メモの入力欄 | 03 §9.1 | `UserNotes` `PromptReviewViewModel` | `PromptComposerTests`（空の項目は行ごと出ない） | 2 | 🔨（**画面は未目視**） |
| 4 章 | 画面のフレームと、システム音声を同時に記録する | 04 §2 / ADR-0016 決定 2 | `CaptureSessionService` | `CaptureSessionServiceTests.録音は撮影と同時に始まり同時に止まる` | 3 | ✅（経路。音の中身は未確認） |
| F-14 | Web Detection（上級者向け・任意） | 05 / ADR-0019〜0022 | `WebDetectionService` `VisionWebDetectionClient` | `WebDetectionGateTests` `WebDetectionServiceTests` `VisionWebDetectionClientTests` | 3' | 🔨（**Vision API を 1 度も呼んでいない**） |

## 5.6 Web Detection（既定オフ）

| 項目 | 設計 | 実現クラス | テスト | 段階 | 状態 |
|---|---|---|---|---|---|
| 既定オフ | 05 §3 / ADR-0021 | `WebDetectionSettings.Enabled` | `WebDetectionGateTests.既定では送らない` `SettingsTests.WebDetectionの既定はオフ` | 3' | ✅ |
| 「高度な設定（上級者向け）」のラベルを画面に出す | 05 §10.1 | `MainWindow.xaml` の `Expander` ＋ `Strings.AdvancedSectionHeader` | — | 3' | 🔨（**画面は未目視**） |
| 有効化時の同意ダイアログ（3 点の説明） | 05 §10.2 / ADR-0021 | `WpfWebDetectionPrompt` `WebDetectionViewModel` | — | 3' | 🔨（**画面は未目視**） |
| **設定ファイルの手書きでは有効にならない** | 05 §3.1 / ADR-0021 決定 1 | `WebDetectionGate` `WebDetectionSettings.ConsentVersion` | `WebDetectionGateTests.有効の印だけ立てても送らない` | 3' | ✅ |
| API キーを平文保存しない（資格情報マネージャー） | 05 §4 / ADR-0020 | `CredentialManagerSecretStore` | `--secret-selftest`（往復・削除を実測） | 3' | ✅（**一覧は未目視**） |
| API キーをログに出さない | 05 §4.3 / ADR-0020 決定 4 | `VisionWebDetectionClient`（例外を短い文に置き換える） | `VisionWebDetectionClientTests.失敗の文に鍵やURLを混ぜない` | 3' | ✅ |
| 送信枚数の上限（既定 3） | 05 §9 / ADR-0022 決定 1 | `WebDetectionSettings.MaxImages` `EvenTimeFrameThinner` | `WebDetectionServiceTests.上限枚数まで間引いて送る` | 3' | ✅ |
| **顔の検出・切り出し・人物特定に使わない** | 05 §5.2 / ADR-0019 決定 2 | `VisionWebDetectionClient`（`WEB_DETECTION` のみ） | `VisionWebDetectionClientTests.要求する機能はWebDetectionだけ` | 3' | ✅ |
| 使う結果は `bestGuessLabels` / `webEntities` / `pagesWithMatchingImages` | 05 §5.3 | `VisionWebDetectionClient.Read` | `VisionWebDetectionClientTests.応答から三つだけ拾う` | 3' | ✅ |
| `web_detection.json` を一時データとして扱う | 01 §6.1 / 05 §6 | `SessionLayout.WebDetectionFile` `FileSystemSessionWorkspace` | `SessionWorkspaceTests`（完全削除） | 3' | ✅ |
| CLAUDE.md に「手がかりの一つにすぎない」と書く | 05 §11 | `analyzer/CLAUDE.md` §4 | — | 3' | ✅ |
| 結果画面に「Web Detection を使用（n 枚送信）」 | 05 §6.3 / §10.3 | `ResultViewModel.WebDetectionNotice` | `WebDetectionDocumentTests`（書いて読める） | 3' | 🔨（**画面は未目視**） |
| エラー時は Web Detection なしで続行し、その旨を表示 | 05 §7 / ADR-0022 決定 5 | `WebDetectionService` `VisionWebDetectionClient` | `WebDetectionServiceTests.失敗しても例外にせず書き残して続ける` | 3' | ✅ |

## 5.3 プロンプト生成と Claude Code 連携

| ID | 要件 | 設計 | 実現クラス | テスト | 段階 | 状態 |
|---|---|---|---|---|---|---|
| F-21 | テンプレートからプロンプトを自動生成（外部ファイル） | 03 §5 / ADR-0014 | `PromptComposer` / `analyzer/prompt-template.md` | `PromptComposerTests`（11 件） | 2 | ✅ |
| F-22 | 送る前に確認・編集できる | 03 §9.1 | `PromptReviewViewModel` `PromptReviewWindow` | `AnalysisServiceTests`（編集後の内容が送られること） | 2 | 🔨（**画面は未目視**） |
| F-23 | `analyzer/` をセッションフォルダへコピーして作業ディレクトリにする | 03 §8 / ADR-0014 | `AnalyzerTemplateDeployer` | 実機（CaptureProbe --analyze） | 2 | ✅ |
| F-24 | `--output-format json` と `--json-schema` で結果を受けてパース | 03 §6 / ADR-0006 / ADR-0012 / **ADR-0038** | `ClaudeCliAnalysisEngine` `AnalysisResultParser` | `AnalysisResultParserTests`（14 件）＋`AnalysisServiceTests`（**プロンプトが引数に載らない**）＋実機 | 2 | ✅ |
| F-25 | 進捗表示とキャンセル（プロセス終了） | 03 §6.4 / §9.2 | `ClaudeCliAnalysisEngine` `AnalysisProgressWindow` | `AnalysisServiceTests`（キャンセルの伝搬） | 2 | 🔨（**画面は未目視**） |
| F-26 | 結果画面（第一候補・他候補・確信度・根拠・未確認点・参考 URL／クリックで開く） | 03 §9.3 | `ResultViewModel` `ResultWindow` `ResultMarkdownWriter` | 実機（Markdown 出力を確認） | 2 | 🔨（**画面は未目視**） |
| F-27 | 追加情報を付けて `--resume <session_id>` で再調査 | ADR-0007 / 03 §6.1 | `ClaudeCliAnalysisEngine` `ResultViewModel` | `ClaudeInvocationArgumentTests`＋実機 | 2 | ✅ |
| F-28 | 履歴一覧（**サムネイルなし**） | 06 §5 / ADR-0024 | `HistoryEntry` `JsonHistoryStore` `HistoryViewModel` | `HistoryStoreTests`（8 件）`HistoryEntryTests`（4 件） | 4 | 🔨（**画面は未目視**） |

## 5.4 Claude Code の起動条件

| 要件 | 設計 | 段階 | 状態 |
|---|---|---|---|
| ツールを `Read,WebSearch,WebFetch` に絞る（書き込み・シェル不可） | ADR-0006 決定 1〜3 | 2 | ✅（`--tools`＋`--allowedTools`＋`--permission-prompts none` の三重。実機で `permission_denials` 0 件） |
| `--max-turns` を設定可（既定を提案） | ADR-0006 決定 5（既定 30） | 2 | ✅（`ClaudeSettings.MaxTurns`。**20 枚で足りるかは未検証**） |
| `claude` のパスを設定可／起動時に `--version` で確認／`.cmd` とネイティブ版の両対応 | 01 §9.1 / 03 §6.2 | 2 | ✅（ネイティブ版で実機確認。**npm 版（.cmd）は未検証**） |
| 標準出力・標準エラーを `claude.log` に保存 | 01 §6.1 / 03 §6.3 | 2 | ✅（UTF-8 BOM つき。起動条件も先頭に書く） |

## 5.5 データ保持

| 要件 | 設計 | 実現クラス | 段階 | 状態 |
|---|---|---|---|---|
| 一時データを「結果画面を閉じた／次の撮影／アプリ終了」で自動的に完全に消す | 02 §6.2 / 06 §4.3 | `FileSystemSessionWorkspace` `CaptureSessionService` `App.OnExit` `MainWindow.OnClosing` | 1（結果画面は 2、畳むときは 4） | ✅（**段階 4 で「ウィンドウを畳んだとき」を足した**。ADR-0025 決定 2） |
| 結果とメモは履歴として残し、個別・一括で消せる | 06 §5 / ADR-0024 | `JsonHistoryStore` `HistoryViewModel`（設定画面の履歴タブ） | 4 | ✅（保存・一覧・削除はテストで固定。**画面は未目視**） |
| 異常終了の残骸を次回起動時に消す | 02 §6.2 | `FileSystemSessionWorkspace.DeleteAll`（`App.OnStartup` から呼ぶ） | 1 | ✅ |
| **メモリで済むものはディスクに書かない** | ADR-0005 | `CaptureSessionService` | 1 | ✅（実機で、捨てたフレームが一度もディスクに触れないことを確認） |
| 「一時データを残す」デバッグ設定（既定オフ） | 02 §7 | `NutLensSettings.KeepTemporaryData` | 1 | ✅（既定が false であることをテストで固定） |
| Claude Code 自身の会話履歴は消さず、README に書く | 01 §7.2 / ADR-0007 / **ADR-0013** | `ClaudeTranscriptLocator` `ResultWindow` の「この解析のデータについて」 | 2（README は 5） | 🔨（**読ませた画像が base64 で残ることを実測済み**。結果画面に場所を出す実装は入ったが画面は未目視） |

## 6 非機能要件

| 要件 | 設計 | 段階 | 状態 |
|---|---|---|---|
| タスクトレイ常駐 | 06 §4 / ADR-0009 / ADR-0025 | 4 | 🔨（起動は通った。**トレイは未目視**） |
| 設定は `%LOCALAPPDATA%\NutLens\settings.json` | 02 §7 | 1 | ✅（アトミック保存・破損時のフォールバックをテスト済み） |
| 撮影中に UI がもたつかない | 02 §8 / 01 §11 | 1 | ✅（フレームプールは FreeThreaded、符号化は背景スレッド、UI へは `IProgress` 経由のみ） |
| 原因が分かるエラーメッセージ | 01 §10 / 06 §7 / ADR-0026 | 1〜4 | 🔨（変換を `ExceptionMessageMapper` 1 箇所へ集約。**単体テストで守れない**（App は `net10.0-windows`、テストは `net10.0`）。**画面は未目視**） |

## 10 配布

| ID | 要件 | 設計 | 段階 | 状態 |
|---|---|---|---|---|
| D-01 | インストーラー（Velopack）／アンインストールで本体と一時フォルダを消す | 07 §3 / ADR-0027 / **ADR-0035** | 5 | ✅（**実機で確認**。GUI でインストール先を選べ、アンインストールで `work` と `settings.json` が消え **`history` と `models` は残る**） |
| D-02 | GitHub Releases からの自動更新（同意を得てから適用） | 07 §5 / §9.1.1 / ADR-0028 / **ADR-0039** | 5 | 🔨（実装済み。**一度も更新していない。** `0.x` は prerelease として出すので、更新確認も prerelease を探す） |
| D-03 | 初回セットアップ（claude の有無・ログイン・Whisper モデル） | 07 §6 / ADR-0029 | 5 | ✅（**実機で確認**。`claude -p` の確認は押したときだけ＝利用枠を勝手に使わない） |
| D-04 | Whisper モデルは同梱せず初回にダウンロード（進捗・中断・再開） | 07 §1 / ADR-0030 / ADR-0034 | 5 | 🔨（**実機で取得を確認**（`large-v3-turbo` 1549 MB）。**中断と再開は未検証。SHA-256 の期待値も未実測**） |
| D-05 | 環境依存の値を固定で書かない／初期値は自動探索 | 01 §9.1 / 07 §2 / **07 §11** / ADR-0033 | 2・5 | ✅（`claude` のパス・モデルのパス・**保存先**・ホットキーすべて設定可。**保存先は段階 5 まで固定で書いていた**＝ADR-0033 で直した。配布の値は `build/Distribution.props` 1 箇所） |
| D-06 | ライセンスを確認し `THIRD_PARTY_NOTICES.md` にまとめる／「このアプリについて」から見られる | 07 §1 / `THIRD_PARTY_NOTICES.md` / `AboutViewModel` | 4 の後 | ✅（配布物に含む依存は 7 つ（Velopack を追加）、**すべて MIT**。採らなかったものと理由も併記。**画面は未目視**） |
| D-07 | 利用枠・送信内容を初回セットアップと README に明記 | 07 §6 / `README.md` / `SetupWindow` / `AboutWindow` | 5 | 🔨（README・セットアップ画面・このアプリについて の 3 箇所に記載。**画面は未目視**） |
| D-08 | 外部送信は Claude Code と（任意の）Web Detection だけ。テレメトリなし | 01 §1.2 / 05 §5.2 | 全段階 | ✅（通信するのは `NutLens.Analysis` の 2 クラスだけ。端点は定数で、設定では変えられない） |
| D-09 | UI は日本語が既定、文字列をリソースに分離 | 02 §11.3 | 1 | ✅（XAML に日本語を直接書いていない。`Strings.resx` のみ） |
| D-10 | ~~SignPath Foundation 申請を前提に~~署名を差し込める構成／`CODE_SIGNING_POLICY.md` | 07 §7 / **ADR-0032** | 5 | ✅（**要件からの意図的な逸脱。2026-11-14 に利用者の承認を得た。** SignPath 前提をやめ、署名の有無も未定。空の段と `CODE_SIGNING_POLICY.md` はある） |
| D-11 | 規約に従うこと・画面キャプチャだけであることを README に | `README.md` の冒頭と「免責事項」 | 4 の後 | ✅ |
| D-12 | GitHub 公開・MIT | `LICENSE` / `THIRD_PARTY_NOTICES.md` | 5 | 🔨（**空の public リポジトリを作成済み**（MixedNutsJP/NutLens）。**中身はまだ push していない**＝コンプラチェック待ち） |
| D-13 | タグを打つと GitHub Actions でビルド〜公開 | 07 §9 / ADR-0031 | 5 | 🔨（`ci.yml` / `release.yml` あり。**一度も回していない**） |
| D-14 | 秘密情報をリポジトリに置かない | 07 §9 | 全段階 | ✅（`.gitignore` で `*.pfx` `*.snk` `*.pem` `*.key` `*.p12` `secrets.json` `.env*` `publish/` `dist/` `release/` を除外。**`*_wpftmp.csproj` も除外**＝WPF のビルドが作る一時プロジェクトに利用者のホームパスが並ぶため。ワークフローは `secrets` 参照なし。API キーは資格情報マネージャー） |
| D-15 | README（日英）・SECURITY・CONTRIBUTING・Issue テンプレート | 07 §8 | 5 | ✅（SECURITY / CONTRIBUTING / Issue テンプレート（不具合・要望）あり。**README は日英併記の 1 本**＝要件の 2 本構成からの逸脱だが、**2026-11-14 に利用者の承認を得た**。07 §8.1） |

---

## 対象外（9 章）

| 項目 | 確認 |
|---|---|
| 動画のダウンロード、URL からの自動取得 | 実装しない。`IFrameSource` の実装は画面キャプチャのみ |
| **顔認識・人物の特定** | 実装しない。Web Detection でも `FACE_DETECTION` と Celebrity Recognition を使わず、顔の切り出しもしない（01 §9.3 / 05 §5.2。要求する機能が `WEB_DETECTION` 1 つだけであることはテストで固定） |
| 動画の再生位置との同期 | `Elapsed` は撮影開始からの経過時間のみ（02 §3） |
| Claude Code の同梱・代理インストール・代理ログイン | 外部プロセスとして呼ぶだけ（01 §4.1） |
