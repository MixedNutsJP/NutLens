# ADR-0008. 画像の縮小・符号化に WIC（WPF Imaging）を使い、System.Drawing と SkiaSharp を入れない

- 状態: **実測・実機で確認**（2026-11-13 承認 / 同日に実機で符号化を確認）
- 日付: 2026-11-13

## 背景

F-06「保存時に長辺 1280px 程度へ縮小する（設定で変えられる）」を満たすには、
BGRA の生バイトから「縮小 → 符号化」を行う部品が要る。.NET には標準の画像処理 API が無いため選択が要る。

制約が 2 つある。

- **D-06**：使ったライブラリのライセンスを確認し、配布してよいものだけを使う。`THIRD_PARTY_NOTICES.md` にまとめる
- **ADR-0006 決定 1**：Claude には Bash が無い。アプリが出力したファイルを Claude がそのまま Read できる必要がある

## 決定

### 1. WPF の Imaging（内部は Windows Imaging Component）を使う

```csharp
BitmapSource source = BitmapSource.Create(width, height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, stride);
TransformedBitmap scaled = new TransformedBitmap(source, new ScaleTransform(ratio, ratio));
JpegBitmapEncoder encoder = new JpegBitmapEncoder { QualityLevel = quality };
encoder.Frames.Add(BitmapFrame.Create(scaled));
encoder.Save(stream);
```

追加の NuGet 依存はゼロ。`PresentationCore` / `WindowsBase` は WPF アプリが既に持っている。

### 2. `NutLens.Capture` に `<UseWPF>true</UseWPF>` を付ける

`WicFrameEncoder` は `NutLens.Capture` に置く（ADR-0002 決定 2：WPF に触るものは Capture）。
そのため `NutLens.Capture` も WPF 参照を持つ。
**UI のクラスは一切入れない。** 使うのは `System.Windows.Media.Imaging` だけ。

### 3. 出力は JPEG（品質 85）にする

| 形式 | 20 枚の合計（長辺 1280px の見積もり） |
|---|---|
| PNG | 20〜40 MB |
| **JPEG 品質 85** | **3〜6 MB** |

Claude に渡す画像なので、ファイルサイズは読み込みの負荷に直結する。
フリップ動画のフレームは写真的な絵で、JPEG の不得手（細い線・平坦な色面）に当たりにくい。
**ただしテロップの文字は細い線なので、品質を下げすぎると OCR（F-12）の精度に響く。**
品質 85 は設定で変更可能にする。

### 4. `TransformedBitmap` は `Freeze()` してからスレッドをまたぐ

WPF の `DispatcherObject` はスレッド親和性を持つ。
`WicFrameEncoder` は撮影用のバックグラウンドスレッドから呼ばれる（非機能要件：UI がもたつかない）。
**`Freeze()` を忘れると、UI スレッド以外から触った時点で `InvalidOperationException` になる。**
生成した `BitmapSource` / `TransformedBitmap` / `BitmapFrame` はすべて `Freeze()` する。

### 5. 縮小は「長辺が指定 px 以下」になる等比縮小。拡大はしない

元画像の長辺が既に 1280px 以下なら、そのまま符号化する（引き伸ばさない）。

## 実装で分かったこと（2026-11-13）

### 1. 実機で通った

| 入力 | 出力 | サイズ |
|---|---|---|
| 2560 × 1440 BGRA（モニタ全体） | **1280 × 720** JPEG | 293 KB |
| 626 × 353 BGRA（窓） | 626 × 353（拡大しない） | 11〜13 KB |

長辺 1280 への等比縮小も、「既に小さいときは拡大しない」も、意図どおりに動いた。
保存した JPEG を開いて、対象ウィンドウの中身が正しく写っていることを目視で確認した。

### 2. `Freeze()` は実際に必要だった

符号化はバックグラウンドスレッドで走る（`SoftwareBitmap` の読み出しの続きとして）。
`BitmapSource.Create` の戻りと `TransformedBitmap` の両方を凍結しているが、
**片方でも凍結を外すとスレッド親和性の例外になる**。決定 4 の想定どおり。

### 3. 読み戻しも WIC でできた

確認ツールで dHash を測り直すために JPEG を BGRA へ戻す必要があったが、
`BitmapFrame.Create` ＋ `FormatConvertedBitmap(PixelFormats.Bgra32)` ＋ `CopyPixels` で足りた。
段階 3 の OCR 用に BGRA を復元する経路も、同じやり方で書ける見込み。

## 影響

- `NutLens.Core` には `IFrameEncoder` の抽象だけを置き、実装を持たない。
  テストでは偽の実装（バイト列をそのまま返す）を差し込む
- WIC は OS の機能なので、Windows 11 のコーデック構成に依存する。JPEG は常にある
- 段階 3 の OCR（`Windows.Media.Ocr`）は `SoftwareBitmap` を要求する。
  JPEG バイト列から `SoftwareBitmap` を作り直す経路が要る（段階 3 の詳細設計で扱う）

## 宿題

### 片付いた宿題

- ~~JPEG 品質 85 でテロップの OCR 精度が落ちないか~~ → **落ちない。**
  実物のフリップ動画 65 枚で日本語の字幕がよく読めた（ADR-0018 実装で分かったこと 1）。
  品質を上げる必要も、符号化前の BGRA で読む必要も無い
- `TransformedBitmap` の縮小品質。WIC の既定は Fant 相当だが、
  `RenderOptions.SetBitmapScalingMode` で明示するかは実測して決める

## 却下した案

| 案 | 却下の理由 |
|---|---|
| `System.Drawing.Common`（GDI+） | .NET 7 以降 Windows 以外で使えなくなった型で、Microsoft 自身が代替への移行を案内している。**非推奨の方向にある API を新規採用する理由がない。** 加えて `Graphics` / `Bitmap` の明示的な `Dispose` 漏れがハンドルリークになりやすく、1 回の撮影で数百回通る経路には向かない |
| SkiaSharp | 高品質だが、native バイナリ（約 10 MB × RID）を配布物に抱える。D-06 のライセンス確認対象が増え、インストーラーも膨らむ。**OS に同じ機能（WIC）がある場面で足す依存ではない** |
| ImageSharp | 2024 年にライセンスが Apache-2.0 から Six Labors Split License へ変わり、商用利用に条件が付いた。個人用ツールの OSS 公開（D-12、MIT）で、利用者の使い方に条件が伝播する依存は避ける |
| Magick.NET | ImageMagick のライセンス（ImageMagick License）確認と、数十 MB の native バイナリ。今回の用途（等比縮小＋JPEG）に対して過大 |
| PNG で保存する | ファイルサイズが 5〜10 倍。Claude が 20 枚読む際の負荷に直結する。可逆である必要が無い（元が動画の圧縮済みフレーム） |
| WebP で保存する | WIC の WebP コーデックは Windows 10 1809 以降に標準搭載されるが、**Claude Code の Read が WebP を画像として扱えるかを確認していない。** 読めなければ解析が成立しない。JPEG にそのリスクは無い |
| 縮小せず原寸で渡す | F-06 に反する。4K フレーム 20 枚で読み込み負荷が跳ね上がる |
