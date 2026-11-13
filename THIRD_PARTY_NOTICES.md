# 使用しているソフトウェア / Third-party notices

NutLens は次のライブラリを使っています。**すべて OSI 承認のライセンスです**（D-06 / D-12）。
NutLens uses the libraries below. **All of them are under OSI-approved licenses.**

## 配布物に含まれるもの / Shipped with the application

| 名前 / Name | 版 / Version | ライセンス / License | 用途 / Purpose |
|---|---|---|---|
| [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime) | 10.0.15 | MIT | 依存の組み立て / Dependency injection |
| [Microsoft.Windows.CsWin32](https://github.com/microsoft/CsWin32) | 0.3.371 | MIT | Win32 API の呼び出しコード生成 / Win32 P/Invoke source generator |
| [NAudio](https://github.com/naudio/NAudio) | 3.3.0 | MIT | システム音声の録音（WASAPI ループバック） / System audio capture |
| [Whisper.net](https://github.com/sandrohanea/whisper.net) | 1.9.1 | MIT | ローカルの文字起こし / Local speech-to-text |
| [Whisper.net.Runtime](https://github.com/sandrohanea/whisper.net) | 1.9.1 | MIT | 上記のネイティブ実装（whisper.cpp） / Native runtime (whisper.cpp) |
| [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) | 2.4.1 | MIT | タスクトレイの常駐アイコン / Tray icon |
| [Velopack](https://github.com/velopack/velopack) | 1.2.161 | MIT | インストール・更新・アンインストール / Installer, updates, uninstall |

`Microsoft.Windows.CsWin32` はビルド時にコードを生成するだけで、
**実行時の DLL は配布物に含まれません。**
`Microsoft.Windows.CsWin32` only generates code at build time; **no runtime DLL ships with the app.**

## 開発時だけに使うもの / Development only

| 名前 / Name | 版 / Version | ライセンス / License | 用途 / Purpose |
|---|---|---|---|
| [xunit.v3](https://github.com/xunit/xunit) | 4.0.3 | Apache-2.0 | 単体テスト / Unit tests |
| [xunit.runner.visualstudio](https://github.com/xunit/visualstudio.xunit) | 4.0.1 | Apache-2.0 | テストの実行 / Test runner |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | 18.12.0 | MIT | テストの土台 / Test platform |
| [Inno Setup](https://jrsoftware.org/isinfo.php) | 6 | Inno Setup License（修正 BSD 相当 / modified BSD-style） | インストーラーの入口画面を作る（[ADR-0035](docs/adr/0035-wrap-the-velopack-setup-in-an-inno-front-end.md)） / Builds the installer front-end |

配布物には含まれません。 / Not included in the distribution.

Inno Setup は**ビルドに使う道具**で、Inno 自身のコードは配布物に入りません。
Inno Setup のライセンスは、これで作ったインストーラーの配布に**表示義務を課しません**。

Inno Setup is a **build-time tool**; its own code is not part of what we ship. Its license
places **no attribution requirement** on the installers it produces.

## 別途ダウンロードするもの / Downloaded separately

| 名前 / Name | ライセンス / License | 備考 / Note |
|---|---|---|
| [Whisper の ggml モデル](https://huggingface.co/ggerganov/whisper.cpp) | MIT | **同梱しません**（D-04）。初回セットアップで利用者がダウンロードします / **Not bundled**; downloaded by the user |
| [Claude Code](https://docs.claude.com/en/docs/claude-code) | — | **NutLens には含まれません。** 利用者が自分で導入し、自分の利用枠で使います / **Not part of NutLens.** Installed and used by the user under their own plan |

## 採らなかったもの / Considered but not used

参考までに、検討して**採らなかった**ものを挙げます。理由は ADR にあります。
For reference, these were considered and **not** used; the reasons are in the ADRs.

| 名前 / Name | ライセンス / License | 理由 / Reason |
|---|---|---|
| Hardcodet.NotifyIcon.Wpf | CPOL 1.02 | **OSI 承認ライセンスではない。** MIT として公開するプロジェクトの依存にすると、利用者に伝わる条件を MIT だけで説明できなくなる（[ADR-0009](docs/adr/0009-use-h-notifyicon-for-tray.md)） / **Not OSI-approved** |
| System.Drawing.Common | MIT | 画像処理は WPF の WIC で足りる。依存を増やさない（[ADR-0008](docs/adr/0008-encode-frames-with-wic.md)） / WIC is enough |
| SkiaSharp | MIT | 同上 / Same |
| Vortice.Direct3D11 / Vortice.DXGI | MIT | CsWin32 と `SoftwareBitmap` に置き換えた（[ADR-0003](docs/adr/0003-capture-with-windows-graphics-capture.md)） / Replaced by CsWin32 |
| Google.Cloud.Vision.V1 | Apache-2.0 | gRPC の依存が増え、**API キー 1 本では認証が通らない**（[ADR-0019](docs/adr/0019-call-vision-web-detection-over-rest.md)） / API-key auth does not fit |
| SQLite | Public Domain | 履歴に索引は要らない（[ADR-0024](docs/adr/0024-store-history-as-one-json-file-per-analysis.md)） / No index needed for the history |

---

各ライブラリのライセンス全文は、それぞれのリポジトリを参照してください。
See each repository for the full license text.
