# ADR-0027. Velopack でインストーラーと自動更新を作り、インストール先をデータの置き場と分ける

- 状態: **実測・実機で確認**（2026-11-14。決定 3 をアンインストールで確認）
- 日付: 2026-11-14

## 背景

D-01「インストーラーを用意する。**Velopack を第一候補とする**（…）。より適した方式があれば
理由を添えて提案すること。**アンインストールでアプリ本体と一時作業フォルダは消える。
履歴を残すかどうかは利用者が選べる**」

D-02「自動更新に対応する。更新元は GitHub Releases とし、起動時に更新を確認して、
利用者の同意を得てから適用する」

第一候補は要件で指定されている。決めるのは次の 3 つ。

1. 本当に Velopack でよいか（「より適した方式があれば提案」とあるので、比べた結果を残す）
2. **どこにインストールするか**
3. アンインストールで何を消し、何を残すか

**2 が効いてくる。** Velopack は既定で `%LOCALAPPDATA%\<パッケージ ID>` に入れる。
パッケージ ID を `NutLens` にすると、インストール先は `%LOCALAPPDATA%\NutLens` になる。

**そこは、このアプリがデータを置く場所そのものである。**

```
%LOCALAPPDATA%\NutLens\settings.json     ← 6 章が置き場を指定している
%LOCALAPPDATA%\NutLens\history\          ← 5.5 が置き場を指定している
%LOCALAPPDATA%\NutLens\work\
%LOCALAPPDATA%\NutLens\models\
```

このまま作ると、**アンインストールが履歴ごと消す。**
D-01 は「履歴を残すかどうかは**利用者が選べる**」と言っているので、要件違反になる。

しかも**気づきにくい。** インストールも起動も更新も普通に動き、
アンインストールしたときに初めて、戻せない形で起きる。

## 決定

### 1. Velopack を使う

| 項目 | 値 |
|---|---|
| ライセンス | MIT（D-06 / D-12 を満たす） |
| 更新元 | GitHub Releases（D-02 が指定） |
| 権限 | **管理者権限を要求しない**（利用者ごとのインストール） |
| 成果物 | `Setup.exe` ＋ 差分パッケージ ＋ ポータブル zip |

比較の結果は却下案に書く。**要件が「第一候補」としか言っていないので、
比べずに決めたことにしない。**

### 2. パッケージ ID を `NutLensApp` にして、インストール先を分ける

```
%LOCALAPPDATA%\NutLensApp\       ← アプリ本体（Velopack が管理する。消してよい）
%LOCALAPPDATA%\NutLens\          ← 利用者のデータ（設定・履歴・モデル・一時データ）
```

**データの置き場は動かせない。** 6 章と 5.5 が `%LOCALAPPDATA%\NutLens\…` と名指ししている。
動かせるのはインストール先のほうなので、そちらを `NutLensApp` にする。

画面に出る名前は `NutLens` のまま（パッケージ ID は利用者が目にするものではない）。

### 3. アンインストールで消すものを明示する

| 対象 | 扱い |
|---|---|
| アプリ本体（`%LOCALAPPDATA%\NutLensApp\`） | Velopack が消す |
| **一時作業フォルダ（`work\`）** | **消す**（D-01）。アンインストールのフックで削除する |
| 設定（`settings.json`） | 消す |
| **履歴（`history\`）** | **残す。** D-01 が「利用者が選べる」と言っているので、**黙って消さない** |
| Whisper のモデル（`models\`） | **残す。** 数百 MB あり、入れ直しに時間がかかる |
| 資格情報マネージャーの API キー | **残す。** 利用者が自分で消せる場所にある（ADR-0020 決定 1） |

**「選べる」を「消さない」で実現する。**
アンインストーラの中で選択肢を出す仕組みは Velopack に無く、
自前で作ると**アンインストール中に動く UI** という最も壊れやすい経路が増える。

残したものの消し方は README と「このアプリについて」に書く。

### 4. `VelopackApp.Build().Run()` を `Main` の**先頭**で呼ぶ

```csharp
[STAThread]
public static void Main(string[] args)
{
    // ここより前に何も書かない。UI も DI も設定の読み込みも後。
    VelopackApp.Build().Run();

    App app = new();
    app.InitializeComponent();
    app.Run();
}
```

**これを呼ばないと、インストール・更新・アンインストールのフックが動かない。**
しかも**アプリは普通に起動する**ので、壊れていることに気づけない。
気づくのは「更新したのに新しくならない」「アンインストールしても残る」という形になる。

WPF は既定で `App.xaml` から `Main` を生成するので、
`<StartupObject>` を自前の `Program` に向ける必要がある。

### 5. ポータブル zip も出す

インストーラーを使いたくない利用者向け。**自動更新は効かない**ことを README に書く。

## 影響

- `Velopack`（MIT）が依存に加わる。**ライセンスは MIT で、D-06 / D-12 と矛盾しない**
- `vpk`（Velopack の CLI）をビルド環境に入れる（`dotnet tool`）
- `App.xaml.cs` の `OnStartup` より前に `Program.Main` ができる
- `IAppDataPaths` は変えない。データの置き場は要件どおり `%LOCALAPPDATA%\NutLens`

## 実装で分かったこと（2026-11-14）

### 1. Velopack 1.2.161 は実在し、最新だった（宿題を 1 つ解消）

版を指定せずに復元して確かめた。`net10.0` 向けの `lib` があり、
`nuspec` の `license` は `expression="MIT"`（D-06 / D-12 を満たす）。

### 2. `vpk` は CLI の名前で、`dotnet tool install -g vpk` で入る

`release.yml` にそう書いた。**ランナーで実際に入るかは未検証。**

### 3. **`vpk` 自身が `VelopackApp.Run()` の有無を検査していた**

決定 4 で「呼び忘れても**アプリは普通に起動するので気づけない**」と書いたが、
**梱包の時点で `vpk` が検査する。**

```
[INF] Verified VelopackApp.Run() in 'System.Void NutLens.App.Program::Main(System.String)'.
```

**懸念していたほど静かには壊れない。** ただし検査されるのは梱包のときだけで、
手元で `dotnet run` している限りは何も言われない。
決定 4 の順序（`Main` の先頭）そのものは変えない。

### 4. `--runtime` を省くと **x86 として梱包される**

```
[WRN] No architecture specified with --runtime, defaulting to x86.
```

**警告は出るが、梱包は止まらない。** `dotnet publish -r win-x64` した中身を
x86 として配ることになる。最初の `vpk pack` で実際に踏んだ。

`release.yml` にも同じ欠落があったので直した（`--runtime win-x64`）。

### 5. self-contained にしていないので `--framework` が要る

`--framework net10.0-x64-desktop` を付けると、.NET 10 デスクトップランタイムが
無い PC で、インストーラーが導入を促す。

**付け忘れると、ランタイムの無い PC で「起動しない」という形で表れる**
（インストールは成功する）。

### 6. 成果物の大きさ（0.1.0・framework-dependent）

| もの | 大きさ |
|---|---|
| `publish/`（45 ファイル） | 35.4 MB |
| `NutLensApp-win-Setup.exe` | **19.9 MB** |
| `NutLensApp-win-Portable.zip` | 12.8 MB |
| `NutLensApp-0.1.0-full.nupkg` | 12.8 MB |

Whisper のモデルは同梱していない（D-04）ので、この大きさに含まれない。

### 7. **インストール先は `--installto` で選べる。ただしコマンドラインだけ**

```
NutLensApp-win-Setup.exe --installto D:\Apps\NutLens
```

`Setup.exe --help` で確認した引数は 5 つ（`--silent` / `--verbose` / `--log` /
`--installto` / `--help`）。**画面からフォルダを選ぶ UI は無い。**

Velopack のソース（`src/bins/src/commands/install.rs`）:

```rust
let (root_path, _root_is_default) = if let Some(path) = install_to {
    (path.clone(), false)                        // ← **そのままルートになる**
} else {
    (Path::new(&appdata).join(&app.id), true)    // 既定は %LOCALAPPDATA%\NutLensApp
};
```

**指定したフォルダの下に ID のサブフォルダを作らない。**
`--installto D:\Apps` と渡すと、`D:\Apps\current\` `D:\Apps\packages\` が直に並ぶ。
**専用の空フォルダを渡す必要がある。**

### 8. **アンインストールはルートの中身を選別せずに全部消す**

`src/bins/src/commands/uninstall.rs`:

```rust
let root_path = locator.get_root_dir();
let _ = remove_dir_all::remove_dir_contents(&root_path);
```

決定 3 で「`work\` を消して `history\` は残す」と決めたが、
**それが成り立つのはデータがインストール先の外にあるからである。**

インストール先にデータを置くと、アンインストールで履歴ごと消える。
ADR-0033 で保存先を分けたのは、結果的にここを守っている。

**インストール先には、こちらから何も書かない。**
`AppContext.BaseDirectory` と `RootAppDir` は読むためだけに使う
（analyzer のひな形・`LICENSE`・保存先の指定）。
書くのは `data-dir.txt` だけで、それは**保存先を変更したときにしか作られない**。

### 9. **決定 3 が実機で成立した**（宿題を 1 つ解消）

利用者が `D:\NutLensApp` に入れ、何度かアンインストールした後の状態を確かめた。

Velopack のログ（`%LOCALAPPDATA%\Velopack\velopack_NutLensApp.log`）:

```
[06:03:45] [INFO] Command: Uninstall
[06:03:45] [INFO] Running --veloapp-uninstall hook...
[06:03:45] [Information] Found fast exit hook: --veloapp-uninstall
[06:03:45] [Information] Completed hook, exiting...
[06:03:45] [INFO] Hook executed successfully (took 114.5209ms)
[06:03:46] [INFO] Removing directory '"D:\\NutLensApp"'
```

**`Program.OnBeforeUninstall` が呼ばれ、114.5 ms で完走している。**
UI も出さず、例外も投げていない。

残り方も意図どおりだった。

| 対象 | 証拠 | 判定 |
|---|---|---|
| `history\` | 4 件が残存。最古は 2:36:45 で、06:03 のアンインストールを跨いでいる | **残る**（D-01） |
| `models\` | `ggml-base.bin` が 1:12:21 のまま生存 | **残る** |
| `settings.json` | **6:04:44 に作成**（以前は 849 バイト、いまは 1192 バイトの別ファイル） | **消えて作り直された** |
| `work\` | 存在しない | **消えている** |
| インストール先 | `D:\NutLensApp` も `%LOCALAPPDATA%\NutLensApp` も無い | **消えている** |

`settings.json` を消すのは `OnBeforeUninstall` だけなので、
**作成時刻が新しいこと自体がフックの実行を示している。**

### 10. `--installto` も実機で通った

ログの `D:\\NutLensApp\\current\\NutLens.exe` から、
指定したフォルダがそのままルートになっていることが確認できる（実装で分かったこと 7）。

### 11. API は ADR の想定どおりだった

`VelopackApp.Build().OnBeforeUninstallFastCallback(…).Run()`、
`UpdateManager(IUpdateSource)`、`CheckForUpdatesAsync` / `DownloadUpdatesAsync` /
`ApplyUpdatesAndRestart` / `WaitExitThenApplyUpdates`、`GithubSource`。

`UpdateManager.IsInstalled` があるので、**開発中の実行やポータブル版では
確認そのものを飛ばせる**（ADR-0028 の `NotInstalled`）。

## 宿題

- **更新を一度も当てていない**（版を 2 つ作らないと試せない）
- **インストール・更新・アンインストールを一度も試していない。**
  特に決定 3 の「`work\` を消して `history\` を残す」は、実際にアンインストールしないと確かめられない
- パッケージ ID を `NutLensApp` にしたときに、Velopack の中で名前の衝突が起きないか
- `--installto` でインストールした後にアンインストールすると、
  保存先を変えていた場合は `data-dir.txt` も消える。**データは残るが、次に入れ直したとき
  既定の場所を見る**ので、保存先を指定し直すことになる（実害は小さいが未検証）
- 差分パッケージ（delta）が Whisper のモデルを含まないこと（同梱しないので含まないはずだが未確認）

## 却下した案

| 案 | 却下の理由 |
|---|---|
| パッケージ ID を `NutLens` のままにする | **インストール先が `%LOCALAPPDATA%\NutLens` になり、データの置き場と重なる。アンインストールが履歴ごと消す**（D-01 違反）。しかもアンインストールするまで気づけない（決定 2） |
| データの置き場を `%LOCALAPPDATA%\NutLens` から動かす | 6 章と 5.5 が置き場を名指ししている。**要件側を曲げることになる** |
| MSIX | **署名が必須。** SignPath の承認前は配布できない（D-10 は「承認されるまでは未署名で配布」と言っている）。サイドロードの手順も利用者に重い |
| WiX / MSI | **管理者権限が要る。** 自動更新も自前で作ることになり、D-02 の実装がまるごと増える |
| Inno Setup | インストーラーとしては良いが、**自動更新の仕組みが無い。** 更新の確認・差分・ロールバックを自前で持つことになる |
| ClickOnce | 署名と配置の制約が強く、Windows の警告も出やすい。.NET の新しい配布では主流から外れている |
| zip のポータブル版だけにする | **D-01 がインストーラーを要求している。** 更新の導線も無くなり D-02 も満たせない |
| アンインストール時に履歴を消すか尋ねる UI を作る | **アンインストール中に動く UI** は最も壊れやすい経路。失敗したときに、消えかけの状態が残る（決定 3） |
| 履歴も黙って消す | D-01 の「残すかどうかは利用者が選べる」に反する。**戻せない** |
| `VelopackApp.Build().Run()` を `App.OnStartup` で呼ぶ | WPF の初期化が先に走る。**インストール直後やアンインストールのフックは UI を出さずに終わる必要がある**ので、`Main` の先頭でなければならない（決定 4） |
