# ADR-0035. インストール先を選ばせるために Inno Setup を前段に置き、Velopack を呼ばせる

- 状態: **採用**（2026-11-14。利用者が選択）
- 日付: 2026-11-14
- 補う対象: [ADR-0027](0027-package-with-velopack-and-separate-install-dir.md)（**決定は覆さない**）
- **決定 4 は [ADR-0036](0036-let-inno-launch-the-app-after-the-wizard-closes.md) が置き換えた**

> **注記（2026-11-14）.** 実物を渡したところ、
> 「inno が終わった後閉じる前に初期設定画面が出てしまう」と指摘を受けた。
> 決定 4（`--silent` で呼ばない）は `--help` の文面だけを見て書いたもので、
> **`--silent` がアプリの自動起動も抑えることを確かめていなかった。**
> **決定 4 は [ADR-0036](0036-let-inno-launch-the-app-after-the-wizard-closes.md) に置き換わっている。**
> 決定 1・2・3・5・6 はそのまま効く。

## 背景

利用者からの指摘。

> ただ、GUI 上で本体のインストール先を変更できたらいいんですが・・・
> CLI 専用だと利用者にとっては使いにくいですよね

ADR-0027 実装で分かったこと 7 のとおり、**Velopack の `Setup.exe` には
フォルダを選ぶ画面が無い。** `--installto` はコマンドラインだけで、
追加する仕組みも用意されていない
（[velopack#1119](https://github.com/velopack/velopack/issues/1119) は
pack 時の指定を求める要望で、利用者が選ぶ話ではない）。

ADR-0027 の却下案では Inno Setup を
**「Velopack の代わりに使う案」**として退けていた。

> Inno Setup｜インストーラーとしては良いが、**自動更新の仕組みが無い。**

**「前段として使う案」を検討していなかった。** それが抜けていた。

### 既存の事例（別言語のデスクトップアプリ）を参照した

維持者が別に持っている JVM 系のデスクトップアプリは Inno Setup で配っている。
コードは流用できないが、**判断は参照できる**。

| 項目 | その事例の判断 | NutLens への当てはめ |
|---|---|---|
| インストール先 | `{localappdata}\Programs\<アプリ名>`、**ディレクトリ選択ページは出す** | 同じ形にできる |
| 権限 | `PrivilegesRequired=lowest`（UAC を出さない／更新も昇格なし） | Velopack と一致 |
| アンインストール | **データを消さない。**「キャッシュは数 GB になりうる」 | モデルが最大 3 GB。同じ判断 |
| 更新 | **自前で実装**（`.part` → SHA-256 → 別プロセスに任せて退く） | **Velopack が肩代わり済み** |

**更新を自前で書く部分だけは真似しない。** あちらは自前サーバのために書いたもので、
GitHub 向けに書き直すと、Velopack が既に持っている
差分更新・照合・ロールバックを捨てて同じものを作ることになる。

## 決定

### 1. Inno は**フォルダを選んで渡すだけ**にする

```
NutLens-Setup.exe（Inno 製）
  ├ ディレクトリ選択ページを出す
  ├ 中に Velopack の Setup.exe を抱えている
  └ 選ばれた場所へ  Setup.exe --installto <選んだフォルダ>  を実行する
         └ 以降のインストール・更新・アンインストールは Velopack が持つ
```

**ADR-0027 の決定はひとつも覆さない。** Velopack は今までどおり
インストールの実体・更新・アンインストールを持つ。Inno が足すのは**入口の画面だけ**。

### 2. Inno は**何も永続的にインストールしない**

| 設定 | 値 | 理由 |
|---|---|---|
| `Uninstallable` | **`no`** | **アンインストールの口を 2 つ作らない** |
| `CreateAppDir` | `yes` | ディレクトリ選択ページを出すために要る |
| `PrivilegesRequired` | `lowest` | UAC を出さない。更新も昇格なしで通る |
| `DefaultDirName` | `{localappdata}\NutLensApp` | Velopack の既定と揃える |

**`Uninstallable=no` が要**。これが無いと「アプリと機能」に
Inno と Velopack の 2 つが並び、**どちらで消せばよいか分からなくなる。**
消すのは Velopack 側だけ。

Inno が `{app}` を作るが、中身は Velopack が入れる。

### 3. **選ばれたフォルダが空でなければ警告する**

ADR-0027 実装で分かったこと 8 のとおり、
**Velopack のアンインストールはルートの中身を選別せずに全部消す。**

```rust
let _ = remove_dir_all::remove_dir_contents(&root_path);
```

`D:\Apps` のような共用フォルダを選ばれると、
**アンインストールでそこにある他人のファイルごと消える。**

空でないフォルダを選んだら、先へ進む前に警告する。
**止めはしない**（意図して選ぶ場合もある）が、黙って通さない。

### 4. Velopack の `Setup.exe` を `--silent` で呼ばない

`--silent` は「すべてのダイアログを隠し、すべての問いに『はい』と答える」。
**.NET ランタイムの導入も黙って進む。**

導入が要るかどうかは利用者に見えたほうがよい。進捗も Velopack 側が出す。
Inno は `waituntilterminated` で待ち、**終了コードを見て失敗を報せる。**

### 5. 配布物は Inno の exe 1 つにする

Releases に Velopack の `Setup.exe` を**別途置かない。**

| 置くもの | 用途 |
|---|---|
| `NutLens-Setup.exe`（Inno） | **これを実行する** |
| `NutLensApp-*-full.nupkg` | 更新に要る（Velopack が読む） |
| `releases.win.json` / `RELEASES` | 同上 |
| `NutLensApp-win-Portable.zip` | インストーラーを使わない人向け |

**「どっちを実行すればいいのか」を作らない。**
Velopack の `Setup.exe` は Inno の中にある。

### 6. 更新の経路は変えない

更新は今までどおり Velopack が GitHub Releases から行う（ADR-0028）。
**Inno は初回のインストールにしか関わらない。**

更新で置き換わるのは `<インストール先>\current\` だけなので、
利用者が選んだ場所はそのまま保たれる。

## 影響

- `installer/NutLens.iss` が増える
- ビルドに **Inno Setup 6** が要る（`winget install JRSoftware.InnoSetup`）。
  **配布物には含まれない**ので `THIRD_PARTY_NOTICES.md` の対象外（D-06）
- `release.yml` に Inno の導入と `ISCC` の実行が増える
- `AppId` の GUID は `3B3321D2-07C4-44F2-950F-76D2F832FEBE`。
  **これを変えない。** 変えると Windows から見て別のアプリになる
- SmartScreen の警告は Inno の exe にも出る（署名していないため。段数は増えない）

## 宿題

- **一度も作っていない・試していない。** Inno が未導入
- `ISCC` を GitHub Actions のランナーに入れる手順（**未検証**）
- Inno の画面と Velopack の進捗が二重に出る見え方（**未目視**）
- 既にインストール済みの状態で Inno 版を実行したときの挙動（**未検証**）。
  Velopack 側は上書き更新になるはずだが確かめていない

## 却下した案

| 案 | 却下の理由 |
|---|---|
| Inno だけにして、更新を自前で書く（上の事例と同じ方式） | **Velopack が持っている差分更新・照合・ロールバックを捨てて作り直すことになる。** 自前サーバ向けに書かれたものを GitHub 向けに書き換える作業が、まるごと増える |
| 自前のランチャー exe を書く（WPF など） | Inno が持っている画面・展開・終了コードの扱いを**自分で書き直すことになる。** 署名と SmartScreen の話も増える |
| CLI の `--installto` だけで済ませ、README に書く | **CLI を触らない利用者は事実上選べない。** 利用者が「使いにくい」と指摘した点がそのまま残る |
| Velopack にフォルダ選択を足す提案を出して待つ | [velopack#1119](https://github.com/velopack/velopack/issues/1119) は未実装で、**いつ入るか分からない** |
| `--msi` で配る | 「machine-wide bootstrap」で、**インストール先は利用者が選べない。** 署名も実質必須になる |
| Inno にもアンインストーラを持たせる | **「アプリと機能」に 2 つ並ぶ。** どちらで消せばよいか分からなくなる（決定 2） |
| Velopack を `--silent` で呼ぶ | **.NET ランタイムの導入まで黙って進む。** 失敗したときに何も見えない（決定 4） |
| Releases に Velopack の `Setup.exe` も置く | **「どっちを実行すればいいのか」が生まれる**（決定 5） |
| 空でないフォルダを選ばせない（弾く） | 意図して選ぶ場合がある。**警告して判断を委ねる**（決定 3） |
