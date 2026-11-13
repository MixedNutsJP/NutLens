# ADR-0036. Velopack を `--silent` で呼び、アプリはウィザードを閉じるときに Inno が起こす

- 状態: **採用**（2026-11-14。利用者が選択）
- 日付: 2026-11-14
- 置き換える対象: [ADR-0035](0035-wrap-the-velopack-setup-in-an-inno-front-end.md) の決定 4

## 背景

ADR-0035 で Inno を前段に置いた。実物を作って利用者に渡したところ、指摘があった。

> 概ね問題ないですが、inno が終わった後閉じる前に初期設定画面が出てしまうんですよね

起きていたこと:

```
1. Inno のウィザード：フォルダを選ぶ → インストール
2. Inno が Velopack の Setup.exe を起動して待つ
3. Velopack がインストールし、**そのままアプリを起動して**終了する
4. NutLens が立ち上がり、**初回セットアップ画面が出る**
5. Velopack が終わったので Inno の「完了」ページが出る   ← もう遅い
```

**ウィザードがまだ出ている最中に、アプリの初回セットアップ画面が重なる。**

### ADR-0035 決定 4 が、事実を確かめずに書かれていた

決定 4 はこう書いていた。

> `--silent` は「すべてのダイアログを隠し、すべての問いに『はい』と答える」。
> **.NET ランタイムの導入も黙って進む。**

これは `--help` の文面から読み取ったもので、**ソースを確かめていなかった。**

Velopack の `install.rs`:

```rust
if !dialogs::get_silent() {
    info!("Starting app...");
    shared::start_package(locator, start_args, Some(constants::HOOK_ENV_FIRSTRUN))?;
}
```

**`--silent` は「ダイアログを出さない」だけでなく「アプリを起動しない」も兼ねている。**
`--help` にはそう書かれていない。

決定 4 は、`--silent` が持つ効果の半分を知らずに却下していた。

## 決定

### 1. Velopack は `--silent` で呼ぶ

```pascal
Exec(SetupPath, '--silent --installto "' + ExpandConstant('{app}') + '"',
     '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
```

**アプリが勝手に起動しなくなる。** これが本題。

決定 4 が心配していた点は、実際には問題にならない。

| 心配したこと | 実際 |
|---|---|
| 進捗が見えない | **Inno が自分の進捗ページを出している。** 無言にはならない |
| 失敗が見えない | **終了コードを見て報せている**（ADR-0035 決定 4 の後半はそのまま効く） |
| .NET ランタイムの導入が黙って進む | 利用者は既に Inno のウィザードで同意している。**そこが同意の場**であって、Velopack の中ではない |

### 2. アプリを起こすのは Inno の `[Run]`。**ウィザードを閉じるときに起きる**

```ini
Filename: "{code:MainExePath}"; Description: "{cm:LaunchProgram,NutLens}"; \
    Flags: nowait postinstall skipifsilent runasoriginaluser; Check: MainExeExists
```

| フラグ | 効き目 |
|---|---|
| `postinstall` | 完了ページの**チェックボックス**として出る |
| `nowait` | 起こしてすぐ Inno が閉じる。**待たない** |
| `skipifsilent` | 静かな実行では起こさない |
| `runasoriginaluser` | 昇格していた場合でも、利用者の権限で起こす |

**順序が変わる。**

```
1. Inno のウィザード：フォルダを選ぶ → インストール
2. Velopack が静かに入れる（アプリは起動しない）
3. 完了ページ：「NutLens を実行する」にチェック
4. 「完了」を押す → **Inno が閉じるのと同時にアプリが起動する**
```

**チェックを外せば起動しない。** 起動するかどうかを利用者が決められる。

### 3. 起こすのはルート直下のスタブ

Velopack はルートに起動用のスタブを置く（`install.rs` の `extract_stubs_to_dir`）。

```
<インストール先>\NutLens.exe          ← これを起こす（スタブ）
<インストール先>\current\NutLens.exe  ← 実体。**更新のたびに入れ替わる**
```

**`current\` の中身を直に指さない。** あそこは更新で丸ごと置き換わるので、
指したものが次の版で無くなる。

スタブが無い場合（`--noStub` で作られた等）に備えて `current\` へ落とすが、
**無ければ `[Run]` の行ごと出さない**（`Check: MainExeExists`）。
押しても何も起きないチェックボックスを出さないため。

## 影響

- `installer/NutLens.iss` に `[Run]` が 1 行増え、`Exec` に `--silent` が付く
- Velopack の進捗（スプラッシュ）は出なくなる。**Inno の進捗ページだけになる**
- ADR-0035 決定 4 は取り消す。決定 1・2・3・5・6 はそのまま効く

## 宿題

- **一度も試していない。** 生成までしか確認していない
- `--silent` のとき、.NET ランタイムが無い PC で何が起きるか（**未検証**）。
  `prompt_and_install_all_missing` が黙って入れるはずだが、
  入れられなかったときに終了コードで分かるかを確かめていない
- スタブ（`<ルート>\NutLens.exe`）が実際に置かれるか（**未目視**）

## 却下した案

| 案 | 却下の理由 |
|---|---|
| ADR-0035 決定 4 のまま（`--silent` を使わない） | **ウィザードの最中に初回セットアップ画面が重なる。** 利用者が指摘した事象がそのまま残る |
| Velopack を待たずに（`ewNoWait`）Inno を先に閉じる | インストールが終わる前に完了ページが出る。**失敗しても「完了しました」と出る** |
| アプリを起動しない（`[Run]` を置かない） | インストール直後に何も起きない。**入ったのかどうかが分からない** |
| `[Run]` に `waituntilterminated` を付ける | Inno がアプリの終了を待つ。**ウィザードが閉じないまま残る**（いまより悪い） |
| `postinstall` を使わず、インストール中に起こす | 戻る。ウィザードの最中にアプリが出る |
| `current\NutLens.exe` を直に起こす | **更新のたびに `current\` は入れ替わる。** 指した先が次の版で無くなる（決定 3） |
| 完了ページを出さずに自動で閉じる（`DisableFinishedPage`） | **「NutLens を実行する」を選ぶ場が無くなる。** 失敗したときの報せ先も消える |
