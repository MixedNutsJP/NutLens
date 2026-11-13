# コード署名の方針 / Code signing policy

最終更新 / Last updated: 2026-11-14

## 現状 / Current state

**NutLens の配布物にコード署名はしていません。**
**NutLens releases are not code-signed.**

そのため、`Setup.exe` を実行すると Windows の SmartScreen が
「WindowsによってPCが保護されました」という警告を出します。
これは**ウイルスが見つかったという意味ではなく**、
署名が無いために発行元を確認できない、という意味です。

Because of that, running `Setup.exe` makes Windows SmartScreen show a
"Windows protected your PC" warning. It does **not** mean malware was found;
it means the publisher cannot be verified because there is no signature.

### 実行する手順 / How to run it anyway

1. 警告の中の「詳細情報」をクリックします。 / Click "More info" in the warning.
2. 「実行」をクリックします。 / Click "Run anyway".

**この手順を踏む前に、ダウンロード元が
[公式の Releases](https://github.com/MixedNutsJP/NutLens/releases) であることを確認してください。**
**Before doing this, make sure you downloaded it from the
[official Releases page](https://github.com/MixedNutsJP/NutLens/releases).**

## 今後の予定 / Plans

**決まっていません。** 署名するかどうかも、する場合にどの方式かも未定です。
**Nothing is decided** — neither whether to sign, nor by what method if we do.

この文書は、決まった時点で更新します。
This document will be updated if and when that changes.

## 署名の代わりに確かめられること / What you can verify instead

署名が無くても、**配布物がどこから来たか**は辿れます。

Even without a signature, **where a release came from** can be traced.

| 確かめられること / What | 方法 / How |
|---|---|
| どのコミットから作られたか / Which commit it was built from | リリースノートに記載 / Stated in the release notes |
| どのビルドで作られたか / Which build produced it | [GitHub Actions の実行記録](https://github.com/MixedNutsJP/NutLens/actions)（誰でも見られます） / The public Actions run log |
| ソースコード / The source | このリポジトリ全体（MIT） / This repository (MIT) |

**リリースの成果物は GitHub Actions でしか作りません**（ADR-0031 決定 1）。
開発者の手元で作ったものを Releases に上げる経路はありません。

**Release artifacts are only ever produced by GitHub Actions** (ADR-0031 decision 1).
There is no path for uploading something built on a developer's machine.

## 自己署名証明書を使わない理由 / Why not a self-signed certificate

**SmartScreen の警告は消えません。** 警告が出るかどうかは証明書の有無ではなく、
その証明書の評判（これまでに何回ダウンロードされ、問題が報告されていないか）で決まります。
自己署名では評判が積み上がらないので、警告は出たままです。

**The warning would not go away.** SmartScreen decides on the certificate's reputation,
not on whether a signature exists, and a self-signed certificate never accumulates one.

それでいて、利用者には「署名されている」と見えます。
**消えない警告と、誤った安心だけが残ります。**

Meanwhile users would see "this is signed".
**Only an unchanged warning and a false sense of safety would remain.**

## 関連 / Related

- [ADR-0032](docs/adr/0032-ship-unsigned-and-keep-signing-vendor-neutral.md) — この方針を決めた記録 / The decision record
- [ADR-0031](docs/adr/0031-release-from-tags-and-leave-a-slot-for-signing.md) — リリースの作り方 / How releases are built
- [SECURITY.md](SECURITY.md) — 脆弱性の報告 / Reporting vulnerabilities
