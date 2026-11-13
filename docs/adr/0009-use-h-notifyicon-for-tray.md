# ADR-0009. タスクトレイに H.NotifyIcon.Wpf を使う

- 状態: **採用**（2026-11-13 承認）— 実装は段階 4
- 日付: 2026-11-13

## 背景

6 章（非機能要件）に「タスクトレイに常駐し、すぐ撮影を始められる」がある。
WPF には通知領域アイコンの標準コントロールが無い（`System.Windows.Forms.NotifyIcon` は WinForms）。

D-06 は「使ったライブラリとモデルのライセンスを確認し、**配布してよいものだけを使う**」、
D-12 は「ライセンスは OSI 承認のもの（MIT を第一候補）とし、
使うライブラリ・モデルのライセンスと矛盾しないことを確認する」。

WPF のトレイ実装で最も広く使われているのは **Hardcodet.NotifyIcon.Wpf** だが、
そのライセンスは **CPOL（The Code Project Open License）1.02** で、
**OSI 承認ライセンスではない。** CPOL には「本ソフトウェアを主たる価値とする形で
再配布・販売してはならない」等の条項があり、MIT プロジェクトの依存としては
利用者への条件の伝播を説明しづらい。

## 決定

### 1. H.NotifyIcon.Wpf（MIT）を使う

`H.NotifyIcon` は Hardcodet.NotifyIcon.Wpf から派生したライブラリで、**MIT ライセンス**。
WPF / WinUI / Uno / MAUI 向けのパッケージが分かれており、WPF 用は `H.NotifyIcon.Wpf`。

### 2. トレイに触るコードを `ITrayIconService` の背後に置く

`NutLens.Core` にインタフェースを置き、実装を `NutLens.App` に置く（ADR-0002）。
ライブラリを差し替える必要が出たとき、変更が 1 クラスに収まる。

### 3. `THIRD_PARTY_NOTICES.md` に記載し、「このアプリについて」画面から開けるようにする

D-06 の要求。段階 5 で全依存をまとめる際、このライブラリも含める。

## 影響

- 実装は段階 4。段階 1〜3 では依存を追加しない（`Directory.Packages.props` には版だけ記載しておく）
- Hardcodet 版の豊富な作例（バルーン通知、リッチなポップアップ）がそのままは使えない場合がある。
  API は概ね互換だが、名前空間が `H.NotifyIcon` になる

## 宿題

- H.NotifyIcon.Wpf 2.4.1 が .NET 10 の WPF で動くかを段階 4 で実機確認する。
  **現時点では NuGet 上に存在することと MIT であることしか確認していない**

## 却下した案

| 案 | 却下の理由 |
|---|---|
| **Hardcodet.NotifyIcon.Wpf** | **CPOL 1.02 で OSI 承認ライセンスではない。** D-12 の「OSI 承認ライセンスとし、使うライブラリのライセンスと矛盾しないことを確認する」を満たせない。CPOL には再配布に関する条項があり、MIT として公開するプロジェクトの依存にすると、利用者に伝わる条件が MIT だけでは説明できなくなる |
| `System.Windows.Forms.NotifyIcon` を直接使う | `<UseWindowsForms>true</UseWindowsForms>` を足して WinForms を抱き込むことになる。コンテキストメニューが WinForms の描画になり、WPF 側の見た目・DPI 対応と揃わない。D-09（文字列をリソースに分離）も WinForms 側で別建てになる |
| `Shell_NotifyIcon` を P/Invoke で自前実装する | アイコンの再登録（エクスプローラの再起動時に `TaskbarCreated` メッセージを受けて登録し直す）、DPI 変更、Windows 11 の通知領域オーバーフローの扱いを自分で持つことになる。**壊れ方が「再起動したらアイコンが消える」という、利用者が原因を特定できない形**になる |
| トレイ常駐をやめ、通常のウィンドウだけにする | 6 章が明示的に要求している |
