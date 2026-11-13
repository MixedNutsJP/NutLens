# NutLens — プロジェクトの作法

ショート動画（フリップ動画）の画面・音声をキャプチャし、ローカルの Claude Code に解析させて
元の作品を特定する Windows デスクトップアプリ。

要件の原典は `implementation-prompt.md`。**要件を自分の判断で変えない。**
変える必要が出たら ADR を起こして承認を得る。

---

## 1. 止まる規則

次のときは**作業を止めて、要約と判断が要る点を渡す**。黙って進めない。

- `implementation-prompt.md` の要件から外れる実装をするとき
- アーキテクチャ上の決定をするとき（ADR の対象になるもの）
- 新しい NuGet 依存を足すとき（**ライセンスの確認が先**。D-06）
- 外部にデータを送る経路を足すとき（D-08。外部送信は Claude Code と、利用者が明示的に有効化した Web Detection だけ）
- `git commit` / `git push` をするとき
  （**`main` は保護されている。直接 push できない。** ブランチ → PR → マージ。ADR-0037）
- **実機の撮影が要るとき。** 撮影は利用者が行う。
  勝手に測り始めず、**何を・何秒・どの設定で撮ってほしいかを先に伝えて待つ**。
  後から「この条件でも測りたい」と追加すると、利用者は動画を再生し直すことになる。
  **必要な撮影は一度にまとめて伝える**

---

## 2. 設計書を先に書き、変更と同時に直す

```
docs/
  design/00-traceability.md     要件ID ↔ 設計 ↔ クラス ↔ テスト の対応表
  design/01-overview.md         概要設計
  design/02-detailed-capture.md 詳細設計（段階 1：キャプチャ）
  design/03-detailed-analysis.md 詳細設計（段階 2：Claude Code 連携）
  design/04-detailed-media.md   詳細設計（段階 3：音声・OCR）
  design/05-detailed-web-detection.md 詳細設計（段階 3'：Web Detection）
  design/06-detailed-ux.md      詳細設計（段階 4：使い勝手）
  design/07-detailed-distribution.md 詳細設計（段階 5：配布）
  adr/README.md                 ADR 索引
  adr/NNNN-….md                 1 ファイル 1 決定
```

### 規則

1. **設計書は実装より先に書く。** 実装してから書いた設計書は、決めたことの記録ではなく後づけの説明になる
2. **設計に関わる変更は、コードと同じ作業の中で該当設計書を直す。**
   クラスを足した・消した・責務を移した・アルゴリズムを変えた・設定項目を増やした → 設計書を直す
3. **`00-traceability.md` の状態欄を更新するまでが 1 つの作業。**
   要件 ID の実現箇所が変わったら表を直す。忘れると表が嘘になる
4. 図は Mermaid で Markdown に直接書く（GitHub がそのまま描画する）
5. 設計書の「最終更新」日付を直す

---

## 3. ADR（アーキテクチャ決定記録）

書き方の型は `~/.claude/skills/adr/SKILL.md` に従う。プロジェクト側の取り決めは次のとおり。

| 項目 | このプロジェクトでの決め |
|---|---|
| 置き場 | `docs/adr/NNNN-kebab-case-english.md` |
| 索引 | `docs/adr/README.md`。**1 行足すまでが 1 つの作業** |
| 言語 | 日本語 |
| 承認 | 状態「提案」で書き、**要約を渡して止まる**。承認後に「採用（YYYY-MM-DD 承認）」へ |
| 粒度 | 1 ファイル 1 決定。覆すときは新しい番号で起こし、旧側を「破棄」にする |
| 却下案 | **必ず 1 つ以上書く。** 「良くない」ではなく「何が起きるか」を書く |
| 誰が書くか | **維持者（この作法の持ち主）。** 第三者の PR に ADR は求めない（ADR-0037 / `CONTRIBUTING.md`） |
| 宿題 | 実測していないことは断定せず `## 宿題` に落とす。索引の「未処理の宿題」にも載せる |

状態の語彙（**強い方へ寄せない**）：提案 / 採用 / 実装済み（動かしていない）/ 実測・実機で確認 / 破棄

---

## 4. コーディング規約

### 4.1 型を明示する（ADR-0010。ビルドで強制される）

| 禁止 | 代わりに |
|---|---|
| `var` | 型名を書く。`foreach` も含む。**ビルドエラーになる**（IDE0007 / IDE0008） |
| `dynamic` | `[ComImport]` インタフェース宣言＋明示的キャスト。**ビルドエラーになる**（`Directory.Build.targets` の検査） |
| 匿名型 `new { … }` | 名前付き `record` を定義する |
| `object` で受け流す | 型付きの `record` へ逆直列化する。JSON の木を歩くなら `JsonElement` |
| `as` ＋ null 検査 | パターンマッチ `is Foo foo`。失敗したら型付き例外を投げる |

ターゲット型 `new()`（`List<int> items = new();`）は左辺に型名があるので**使ってよい**。
`global using` も型名を隠さないので**使ってよい**。

`dynamic` をどうしても書く必要がある行には `// NUTLENS-ALLOW-DYNAMIC` を付ける（理由をコメントで添える）。

### 4.2 オブジェクト指向

- **1 クラス 1 責務。** 「撮る」「重複を判定する」「間引く」「符号化する」「置き場を管理する」は別クラス
- **外界に触る実装はインタフェースの背後に置く**（WinRT / Win32 / native / 外部プロセス / HTTP / ファイル）。
  抽象は `NutLens.Core`、実装は `NutLens.Capture` など（ADR-0002）
- **具体型を `new` するのは合成ルート（`App.xaml.cs`）だけ。** ViewModel は抽象だけを受け取る
- **状態を持つ `static` を作らない。** `static` は純粋関数のヘルパ（`DHash.Compute`）に限る
- 値は `record` / 読み取り専用にする。途中で書き換えない
- 例外は型で表す（`NutLensException` 派生）。UI 層はメッセージへの変換だけを担う

### 4.3 層の向き

```
NutLens.App  →  NutLens.Capture  →  NutLens.Core
```

逆向きの参照をしない。`NutLens.Core` の TFM は `net10.0`（`-windows` なし）なので、
**Core に Windows 専用 API を書くとビルドが落ちる。** これは仕様（ADR-0002）。

### 4.4 その他

- `Nullable` 有効、警告はエラー扱い
- コメントは日本語。**「何をしているか」ではなく「なぜそうしたか」を書く**
- UI の文字列は `NutLens.App/Resources/Strings.resx` に置く。**XAML に日本語を直接書かない**（D-09）
- P/Invoke は `Microsoft.Windows.CsWin32`（`NativeMethods.txt`）で生成する。手書きしない
- NuGet のバージョンは `Directory.Packages.props` で一元管理する

---

## 5. データの扱い（要件 5.5。最も壊してはいけないところ）

- 撮影した画像・音声と、そこから作ったデータは**一時データ**。
  結果画面を閉じたとき／次の撮影を始めたとき／アプリを終了したとき／
  **メインウィンドウを閉じて（トレイへ畳んで）消したとき**に**完全に消す**
  （4 つめは ADR-0025 決定 2 で足した。要件の 3 つに対して増やす方向に倒している）
- **メモリで済むものはディスクに書かない**（ADR-0005）。
  書かなかったものは消す必要がない＝消し漏れが原理的に起きない
- 削除の責務は `ISessionWorkspace` 1 クラスに閉じる。消す処理を他所に書かない
- **履歴に画像のサムネイルを持たない**（F-28）。
  **文字起こしと OCR も履歴に残さない。** どちらも撮影した音声・画像から作ったデータで、
  5.5 では一時データの側にある（ADR-0024 決定 3）
- Claude Code 自身の会話履歴（`%USERPROFILE%\.claude\projects\…`）は**アプリから消さない**。
  README で `claude purge` を案内する

---

## 6. 実装の段階

| 段階 | 範囲 | 状態 |
|---|---|---|
| 1 | キャプチャ（F-01〜F-07。F-04 ホットキーは段階 4） | **完了**（実機で撮影を確認。テスト 78 件成功） |
| 2 | Claude Code 連携（F-21〜F-27、`analyzer/` 一式） | **完了**（通し確認済み。画面は未目視） |
| 3 | 音声・OCR（F-11〜F-13） | **実装完了**（OCR は実物で確認。録音と文字起こしの通し確認は未了） |
| 3' | Web Detection（F-14 / 5.6） | **実装完了**（判定・書式はテストで固定。**Vision API は 1 度も呼んでいない。画面は未目視**） |
| 4 | 使い勝手（F-28・ホットキー・トレイ・設定画面・自動削除・エラー処理） | **実装完了**（テスト 254 件成功。起動とホットキー登録は実機で確認。**画面は未目視**） |
| 5 | 配布（10 章） | **実装完了**（テスト 280 件成功）。**インストーラー・更新・モデルのダウンロード・ワークフローは一度も動かしていない。** 空の public リポジトリを作成済み（未 push） |

**各段階の終わりで、何ができて何が残っているかを報告する。**

---

## 7. よく使うコマンド

```powershell
dotnet build NutLens.slnx -c Debug
dotnet test
dotnet run --project src/NutLens.App
```

ソリューションは `.slnx`（.NET 10 の既定）。`dotnet test` は `global.json` の
`test.runner` で Microsoft.Testing.Platform を選んでいる。これが無いと
「Testing with VSTest target is no longer supported」で失敗する。

GUI を触らずにキャプチャ経路を確かめるツール（製品には含めない）:

```powershell
# モニタ 1 を 8 秒・間隔 1 秒・上限 5 枚で撮る
dotnet run --project tools/NutLens.CaptureProbe -- 8 1 5

# 1 秒ごとに中身が変わる窓を自分で出して撮る（引数は 秒数 間隔 最大枚数 しきい値）
dotnet run --project tools/NutLens.CaptureProbe -- 12 1 8 0 --animate --keep
```

保存された各フレームの dHash と最小距離を出す。
**重複しきい値は推測で決めず、これで測る。**

```powershell
# 撮影を伴わない確認（動画の再生が要らない）
dotnet run --project tools/NutLens.CaptureProbe -- --env                  # 使えるもの一覧
dotnet run --project tools/NutLens.CaptureProbe -- --ocr "<frames>"       # 保存済みフレームに OCR
dotnet run --project tools/NutLens.CaptureProbe -- --hashes "<frames>"    # dHash と距離
dotnet run --project tools/NutLens.CaptureProbe -- --grid "<frames>"      # 格子の細かさの比較
dotnet run --project tools/NutLens.CaptureProbe -- --transcribe "<wav>"   # 文字起こし
dotnet run --project tools/NutLens.CaptureProbe -- 3 --audio-idle         # 録音（音は出ない）
dotnet run --project tools/NutLens.CaptureProbe -- --secret-selftest      # 資格情報の往復
dotnet run --project tools/NutLens.CaptureProbe -- --hotkey-selftest      # ホットキーの登録
```

### 鍵まわり（段階 3'）を触ったら必ず走らせる

```powershell
dotnet run --project tools/NutLens.CaptureProbe -- --secret-selftest
```

本物の鍵には触らない（`NutLens/SelfTest` という別名で書いて読んで消す）。
確かめているのは `CredentialBlobSize` が**バイト長**であること。
**文字数を入れると鍵の末尾が切れ、保存も読み出しも成功したまま、
Google に拒否されて初めて分かる**（ADR-0020 実装で分かったこと 2）。

録音と同じく、単体テストでは守れない。
`CredWriteW` は Windows 専用 API で、テストは `net10.0`。

### ホットキーまわりを触ったら必ず走らせる

```powershell
dotnet run --project tools/NutLens.CaptureProbe -- --hotkey-selftest
```

ウィンドウは出ない。メッセージ専用ウィンドウを作って登録し、すぐ外す。
**その組み合わせがこの PC で空いているか**も分かる。

録音・資格情報と同じく単体テストでは守れない。
`RegisterHotKey` は Windows 専用 API で、テストは `net10.0`。

### Web Detection の送信（**課金される**）

```powershell
dotnet run --project tools/NutLens.CaptureProbe -- --env                  # 3 条件の揃い具合（通信しない）
dotnet run --project tools/NutLens.CaptureProbe -- --web-detect "<frames>"  # 実際に送る（既定 3 枚）
```

**利用者の Google Cloud に課金される。** 勝手に走らせない。

### 録音まわりを触ったら必ず走らせる

```powershell
dotnet run --project tools/NutLens.CaptureProbe -- 3 --audio-idle
```

3 秒ぶんが 94 KB 前後（16 kHz・モノラル・16 bit）なら妥当。
**桁が違っていたら書き出しが暴走している。**
実際に `BufferedWaveProvider.ReadFully` の既定値（true）で
1.2 GB の WAV が生成されたことがある（ADR-0016 実装で分かったこと 2）。

単体テストでは守れない。NAudio は `NutLens.Capture`（`net10.0-windows`）にあり、
テストは `net10.0` なので参照できない。**この確認が唯一の歯止め。**

```powershell
# 段階 2 の通し確認（analyzer の配置 → プロンプト生成 → claude -p → 結果のパース）
# --resume を足すと再調査（F-27）も試す。利用者の利用枠を使うので枚数とターン数を絞ること
dotnet run --project tools/NutLens.CaptureProbe -- 10 --analyze "<セッションフォルダ>" --resume
```
