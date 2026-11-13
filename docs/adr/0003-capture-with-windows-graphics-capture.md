# ADR-0003. 画面の取得に Windows.Graphics.Capture を使い、矩形はモニタ取得後の切り出しで実現する

- 状態: **実測・実機で確認**（2026-11-13 承認 / 同日に実機で撮影を確認）
- 日付: 2026-11-13

## 背景

F-01 は「キャプチャ対象をウィンドウ単位、または画面の矩形で選べる。Windows.Graphics.Capture API を使う」
と、API まで指定している。ただし **Windows.Graphics.Capture（以下 WGC）が取れる単位は
`GraphicsCaptureItem` であり、それはウィンドウ（HWND）かモニタ（HMONITOR）のどちらかしかない。**
「画面の任意の矩形」を直接指定する入口は無い。

したがって「矩形をどう実現するか」は決めないと実装に入れない。

対象は「ブラウザや TikTok アプリで再生中のショート動画」なので、次の性質が要る。

- 対象ウィンドウが他のウィンドウに隠れていても、**そのウィンドウの中身だけ**が取れる
- 動画再生（ハードウェアデコード／オーバーレイ）の中身が黒く抜けない
- 1 秒あたり数十枚の取得に耐える（撮影間隔は 0.5 秒まで指定できる＝F-02）

## 決定

### 1. WGC を使い、`IGraphicsCaptureItemInterop` で対象を直接作る

WPF には `GraphicsCapturePicker`（WinRT のピッカー UI）を出すための `IInitializeWithWindow` 経路もあるが、
F-01 は「選べる」ことだけを求めており、一覧は自前 UI で出す（ADR-0002 の `ICaptureTargetProvider`）。
よって `GraphicsCaptureItem` は COM インタフェース `IGraphicsCaptureItemInterop` の
`CreateForWindow(HWND)` / `CreateForMonitor(HMONITOR)` から作る。

`dynamic` は使わず、`ComImport` 属性付きのインタフェース宣言と明示的なキャストで書く（ADR-0010）。

### 2. 「画面の矩形」は、モニタ全体を WGC で取ってから切り出す

利用者が全画面オーバーレイでドラッグした矩形を**モニタ相対座標**に直し、
`IFrameSource` を包む `CroppingFrameSource`（デコレータ）が各フレームを切り出す。

```
WgcFrameSource(モニタ)  ──包む──▶  CroppingFrameSource(矩形)  ──▶  上位層
```

切り出しは CPU 側の BGRA バッファに対する行コピーで行う。
上位層（`CaptureSessionService`）は包まれているかどうかを知らない。

### 3. 黄色い枠とマウスカーソルを消す（取れる環境でのみ）

| 設定 | 値 | 必要な OS |
|---|---|---|
| `GraphicsCaptureSession.IsCursorCaptureEnabled` | `false` | 10.0.19041 以降 |
| `GraphicsCaptureSession.IsBorderRequired` | `false` | **10.0.22000 以降** |

`IsBorderRequired` は下限 TFM（19041）には無いので、
`ApiInformation.IsPropertyPresent` で存在を確かめてから設定する。
**この確認を飛ばすと Windows 10 で `MissingMethodException` になり、落ちるのは撮影開始の瞬間になる。**

### 4. フレームの間引きは `FrameArrived` の中で行う

WGC はフレームプールに新しい内容が来たときに `FrameArrived` を発火する（概ね対象の更新間隔）。
撮影間隔（0.5〜10 秒）は、これを**ハンドラ内で時刻判定して捨てる**ことで実現する。

```
FrameArrived:
    frame = framePool.TryGetNextFrame()
    経過 = clock.Now - 撮影開始
    if 経過 - 前回採用時刻 < 撮影間隔:
        frame.Dispose()       ← GPU テクスチャをそのまま捨てる。CPU へのコピーはしない
        return
    ステージングテクスチャへコピー → BGRA を取り出す → Channel へ投入
```

**別タイマーを持たない。** タイマー方式にすると「タイマーが起きた瞬間の最新フレーム」を
別スレッドから掴む必要があり、フレームプールの寿命管理と競合する。
再生中の動画が対象なのでフレームは必ず届く。

### 5. 対象の消失とサイズ変更を明示的に扱う

- `GraphicsCaptureItem.Closed` → `CaptureTargetLostException` を上位へ（非機能要件「キャプチャ対象が閉じられた」）
- `Item.Size` と届いたフレームの `ContentSize` が食い違ったら `framePool.Recreate(...)` でプールを作り直す。
  **作り直さないと、以後のフレームが引き伸ばされた／切れた絵になる**（ウィンドウのリサイズや最大化で起きる）

## 実装で分かったこと（2026-11-13）

### 1. Vortice をやめ、CsWin32 ＋ SoftwareBitmap にした

当初は D3D11 の束縛に Vortice.Direct3D11 / Vortice.DXGI（計 2 依存）を使う前提だった。
実装してみると、**この用途で D3D11 に直接触るのは「デバイスを 1 つ作る」ことだけ**で済むと分かった。

| 工程 | 当初の想定 | 実際 |
|---|---|---|
| D3D11 デバイスの作成 | Vortice | **CsWin32 が生成する `D3D11CreateDevice`**（既に依存にある） |
| GPU → CPU の画素取り出し | ステージングテクスチャを確保して `CopyResource` → `Map`/`Unmap` | **`SoftwareBitmap.CreateCopyFromSurfaceAsync`**（WinRT。テクスチャ管理を書かずに済む） |
| バイト列の取り出し | `Map` したポインタから手でコピー | `SoftwareBitmap.CopyToBuffer` → `DataReader.FromBuffer` |

結果、**Vortice の依存 2 つを丸ごと落とせた**（D-06 のライセンス確認対象も 2 つ減る）。
テクスチャの寿命管理を自分で書かないぶん、落ち方の分かりにくい不具合も減る。

代わりに画素が `SoftwareBitmap` → `IBuffer` → `byte[]` と 2 回余分にコピーされる。
採用するフレームだけが通る経路（撮影間隔で間引いた後）なので、実測するまでは許容する。

### 2. 読み出しの多重起動を止める必要があった

`SoftwareBitmap.CreateCopyFromSurfaceAsync` は非同期なので、
`FrameArrived` の中で待たずに投げると**前の読み出しが終わる前に次が始まる**。
`SemaphoreSlim.Wait(0)` で入れなかったフレームは見送る形にした。
ハンドラ内で待つと、以後のフレーム配信そのものが遅れる。

### 3. 「画面の矩形」の切り出しは Core に移した

`CroppingFrameSource` は画素の並べ替えだけで Windows に依存しない。
ADR-0002 決定 2（純ロジックは Core）に沿って `NutLens.Core.Capture` へ移動し、
単体テスト（寸法・画素の一致・クランプ・負座標）を書けるようにした。

### 4. HDCP / DRM で保護された再生は黒画面になる（利用者からの指摘）

段階 1 の実機確認中、**利用者は U-NEXT を視聴していた**。
HDCP で保護された映像は、WGC・Desktop Duplication・GDI のいずれでも
**画面全体または該当領域が黒く抜ける**（OS とドライバがそう作られている。回避しない）。

つまり下の測定でモニタ全体が「ほとんど動かない絵」だったのは、
**動きが無かったのではなく、動いている部分が黒だった可能性が高い。**
モニタ撮影で採用が 1 枚だった結果は、この条件を踏まえて読む必要がある
（ウィンドウ撮影の確認は自前の窓を対象にしたので、この影響を受けていない）。

対象への影響:

| 対象 | 見込み |
|---|---|
| TikTok / YouTube Shorts（ブラウザ・アプリ） | **問題なく撮れる見込み**（HDCP を使っていない）。これが本来の対象 |
| Netflix / U-NEXT / Amazon Prime などの DRM 付き配信 | **黒画面になる。** 対象外（そもそも 9 章でダウンロードも対象外） |

**この制約は回避しない。** 保護の迂回は目的ではないし、9 章の対象外でもある。
代わりに、黒画面になったことを利用者が分かるようにする（宿題）。

### 5. 実機での測定（2026-11-13、Windows 11 26300、4 モニタ構成）

```
WGC 利用可否: True
モニタ: 4 台（2560x1440 / 2560x1440 / 1440x2560 / 1920x1080）
ウィンドウ列挙: 5 件（cloaked / ツールウィンドウ除去後）

モニタ 1 を 8 秒・間隔 1 秒で撮影
  受信 9 枚 → 採用 1 枚（静止した画面なので重複除去が効いた＝正しい挙動）
  frame_001_t0000.0s.jpg  293 KB  1280 x 720   ← 2560x1440 から長辺 1280 へ縮小
```

- `IsBorderRequired = false` は例外なく通った。**枠が実際に消えたかは目視していない**
- ウィンドウ単位の撮影も確認（確認ツールの `--animate`）。
  撮った JPEG を開いて、対象ウィンドウの中身が正しく写っていることを目視した

## 影響

- `NutLens.Capture` は Direct3D 11 デバイスを持つことになる（ADR-0008 が扱う）
- モニタ全体を取ってから切り出すため、矩形が小さくてもモニタ解像度分の GPU→CPU コピーが走る。
  4K モニタ・0.5 秒間隔で 1 回あたり約 33 MB のコピー。実測していないので宿題に落とす
- `IsBorderRequired` が効かない環境（Windows 10）では、録画枠がフレームに写り込む可能性がある。
  写り込んでも Claude の判定材料は画面内容なので致命的ではないが、README に書く

## 宿題

- **2560 × 1440 以上 × 0.5 秒間隔での CPU 使用率と読み出し時間を実測する。**
  `SoftwareBitmap` 経由で画素が 2 回余分にコピーされるため（実装で分かったこと 1）、
  重いようなら `IMemoryBufferByteAccess` で直接読む形に変える
- `IsBorderRequired = false` が実際に枠を消すか。**例外が出ないことしか確かめていない（未目視）**
- Vortice を外したことで `D3D_FEATURE_LEVEL` の指定が既定任せになっている。
  古い GPU で `D3D_DRIVER_TYPE_HARDWARE` が失敗する場合の WARP 退避が実際に効くかは未検証
- **HDCP で黒画面になったことを利用者に伝える方法（段階 4）。**
  「採用フレームがすべて真っ黒に近い」ことは検出できる（dHash が 0 かつ平均輝度が極端に低い）。
  黙って「候補なし」で終わるより、原因を出したほうがよい。実装するかは段階 4 で決める
- **TikTok / YouTube Shorts の実物で撮れることの確認（未了）。**
  段階 1 の確認はすべて自前の窓とデスクトップで行っており、**本来の対象では一度も試していない**

## 却下した案

| 案 | 却下の理由 |
|---|---|
| DXGI Desktop Duplication (`IDXGIOutputDuplication`) | **モニタ単位しか取れず、ウィンドウ単位（F-01 の前半）が実現できない。** 対象ウィンドウが他のウィンドウに隠れると、手前のウィンドウが写る |
| `BitBlt` / `PrintWindow`（GDI） | ハードウェアデコードで再生中の動画が**黒く抜ける**（オーバーレイ面が GDI から見えない）。フリップ動画の中身が取れないので目的を達しない |
| FFmpeg の `gdigrab` / `ddagrab` を外部プロセスで回す | gdigrab は上と同じ黒抜けの問題。ddagrab は Desktop Duplication と同じ制約。加えて FFmpeg バイナリ（GPL / LGPL）の同梱はライセンス整合性の確認が増え、D-06 の「配布してよいものだけを使う」の手間が上がる |
| `GraphicsCapturePicker`（WinRT 標準のピッカー）を使う | 選択 UI の見た目と文言を制御できず、D-09（文字列をリソースに分離して英語を足せる）と噛み合わない。また「画面の矩形」をドラッグで選ぶ流れに組み込めない |
| 矩形を WGC に直接指定する | **そういう API が無い。** `GraphicsCaptureItem` はウィンドウかモニタのみ |
| 矩形用にダミーの透明ウィンドウを作ってそれを `CreateForWindow` に渡す | 透明ウィンドウ自身の中身（＝何も無い）が取れるだけで、背後の画面は取れない |
| タイマーで定期的に最新フレームを掴む | フレームプールの `TryGetNextFrame` をハンドラ外の別スレッドから呼ぶことになり、`Recreate` や `Dispose` と競合する。**落ちるのは撮影中のランダムな時点**になり、原因の切り分けが難しい形の不具合になる |
