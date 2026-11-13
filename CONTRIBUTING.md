# 開発に参加する / Contributing

**個人で作っているソフトウェアです。** 返信までに時間がかかることがあります。
**This is made by one person**, so replies may take a while.

## 先に読んでほしいもの / Read these first

このリポジトリには、**なぜそう作ったか**と**何を確かめていないか**が書いてあります。

This repository records **why things are built the way they are** and
**what has not been checked**.

| 文書 / Document | 中身 / Contents |
|---|---|
| [docs/adr/README.md](docs/adr/README.md) | 決定の一覧と**未処理の宿題** / Decisions and **open questions** |
| [docs/design/00-traceability.md](docs/design/00-traceability.md) | 要件 ↔ 設計 ↔ クラス ↔ テスト / Requirements to code |
| [CLAUDE.md](CLAUDE.md) | **維持者の作法。** 寄稿の条件ではありません / The **maintainer's** working rules — not requirements for contributors |

**読まなくても PR は出せます。** 読むと「なぜこうなっているか」が分かる、という位置づけです。
**寄稿者にお願いすることは、下の「変更を出す前に」だけです。**

**You can open a PR without reading any of them.** They explain *why* things are the way
they are. **The only things asked of contributors are in "Before you open a pull request".**

## ビルドとテスト / Build and test

```
dotnet build NutLens.slnx -c Debug
dotnet test
```

.NET 10 SDK と Windows が要ります。 / Requires the .NET 10 SDK and Windows.

`main` は保護されています。**直接 push できません**（維持者も含みます）。
変更はブランチを切って PR にしてください（[ADR-0037](docs/adr/0037-keep-adrs-public-and-protect-main-instead-of-requiring-them.md)）。

`main` is protected — **nobody pushes to it directly**, including the maintainer.
Work on a branch and open a pull request.

## 変更を出す前に / Before you open a pull request

### 1. ビルドが**警告ゼロ**で通ること

`TreatWarningsAsErrors` が有効です。警告はビルドを落とします。
Warnings are errors; the build will fail.

### 2. `var` と `dynamic` を使わないこと

**ビルドエラーになります**（[ADR-0010](docs/adr/0010-enforce-explicit-types.md)）。
型名を書いてください。`List<int> items = new();` のような左辺に型がある形は使えます。

**They are build errors** ([ADR-0010](docs/adr/0010-enforce-explicit-types.md)).
Write the type name. Target-typed `new()` is fine because the type is on the left.

### 3. 設計書は、直せれば助かります（必須ではありません）

クラスを足した・消した・責務を移した・設定項目を増やした場合、
`docs/design/` の該当する文書と `00-traceability.md` が実物とずれます。

**直していただけると助かりますが、必須ではありません。**
**迷ったら PR の本文に「設計書は未更新です」と書いてください。維持者が直します。**

**整合を保つ最終的な責任は維持者にあります。** 寄稿者の善意に頼る形にはしません。

If you add or remove a class, move a responsibility, or add a setting, the documents under
`docs/design/` and `00-traceability.md` go out of step with the code.

**Updating them is welcome but not required.** If in doubt, just say "docs not updated" in
the pull request — the maintainer will handle it.
**Keeping them in step is the maintainer's responsibility**, not something we leave to
contributors' goodwill.

### 4. ほとんどの変更に ADR は要りません

不具合の修正・テストの追加・誤字・小さな改善は、**そのまま PR を出してください。**

**Most changes need no ADR.** Bug fixes, tests, typos and small improvements —
just open the pull request.

お願いするのは 1 つだけです。

There is only one thing we ask:

> **`docs/adr/` が決めたことを、黙って変えないでください。**
>
> **Do not silently change something `docs/adr/` has decided.**

変えたい場合は、先に Issue で「**現状の決定がなぜ成り立たないか**」を教えてください。
**ADR は維持者が書きます。** 寄稿者に書いていただく必要はありません。

If you want to change one, open an issue first and tell us **why the current decision no
longer holds**. **The maintainer writes the ADR** — you do not have to.

どの決定に触れるのか分からない場合も、**そのまま PR を出して、本文にそう書いてください。**
こちらで確かめます。

If you are not sure whether your change touches a decision, **open the PR anyway and say so**
in the description. We will check.

### 5. 確かめていないことを「動く」と書かないこと

**このリポジトリで一番大事な作法です。**
ADR の状態欄も、設計書も、README の「動作確認の範囲」も、
**実際に動かして確かめたことと、そうでないことを区別して書いています。**

**This is the most important rule here.** The ADR status fields, the design documents
and the README's "what has been checked" table all distinguish **what was actually run
and verified** from what was not.

「動くはず」を「動いた」と書かないでください。
後から誰かがそれを根拠に判断します。

Do not write "it works" for "it should work". Someone will rely on it later.

## 単体テストで守れないもの / What unit tests cannot cover

テストは `net10.0` で、Windows 専用 API を持つプロジェクトを参照できません
（[ADR-0002](docs/adr/0002-split-layers-core-capture-app.md)）。
次を触ったら、**対応する自己診断を必ず走らせてください。**

The tests target `net10.0` and cannot reference the Windows-only projects.
If you touch these, **run the matching self-check.**

| 触ったもの / If you changed | 走らせるもの / Run this |
|---|---|
| 録音（NAudio） | `dotnet run --project tools/NutLens.CaptureProbe -- 3 --audio-idle` |
| 資格情報マネージャー | `dotnet run --project tools/NutLens.CaptureProbe -- --secret-selftest` |
| ホットキー | `dotnet run --project tools/NutLens.CaptureProbe -- --hotkey-selftest` |
| 環境の確認 | `dotnet run --project tools/NutLens.CaptureProbe -- --env` |

**録音は、これが唯一の歯止めです。** 実際に 1.2 GB の WAV が生成されたことがあります
（[ADR-0016](docs/adr/0016-record-system-audio-as-16khz-mono.md)）。

**For the recorder this is the only guard.** A 1.2 GB WAV really did get produced once.

## やらないこと / Out of scope

次は**要件として実装しません。** 提案されても取り込めません。

These are **deliberately not implemented** and cannot be accepted:

- **顔の検出・顔の切り出し・人物の特定** / Face detection, face cropping, identifying people
- **動画のダウンロード** / Downloading video
- **アプリ独自のテレメトリ** / Telemetry
- **履歴に画像のサムネイルを持つこと** / Thumbnails in the history

## ライセンス / License

変更を出した時点で、[MIT ライセンス](LICENSE)で提供することに同意したものとします。
By contributing you agree that your contribution is provided under the [MIT License](LICENSE).
