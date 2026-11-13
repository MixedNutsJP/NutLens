# ADR-0010. 型の明示を強制し、`var` と `dynamic` をビルドエラーにする

- 状態: **実測・実機で確認**（2026-11-13 承認 / 同日に「実際にビルドが落ちること」を確認）
- 日付: 2026-11-13

## 背景

利用者（このプロジェクトの発注者）からの明示的な指示:

> 原則として any や var などの動的な片付けを禁止します

C# における該当物を整理すると次のようになる。

| 書き方 | 性質 | 扱い |
|---|---|---|
| `var x = …` | 静的型。コンパイラが推論する。**読む人には型名が見えない** | 禁止 |
| `dynamic x = …` | **実行時バインド。** 型の誤りがコンパイル時に出ず、実行時例外になる | 禁止 |
| `object x = …` で受けて都度キャスト | 静的型だが、型情報を一旦捨てている | 禁止（受け流しとして） |
| 匿名型 `new { A = 1 }` | 型名を書けない。`var` が必須 | 禁止 |
| `as` + null 検査 | 失敗が `null` に化け、使う場所で `NullReferenceException` になる | パターンマッチに置き換える |

指示を「規約を文書に書く」だけで終わらせると、守られているかどうかは人の注意力に依存する。
**破れる規約は、半年後には破れている。**

## 決定

### 1. `var` を `.editorconfig` でビルドエラーにする

```ini
csharp_style_var_for_built_in_types = false:error
csharp_style_var_when_type_is_apparent = false:error
csharp_style_var_elsewhere = false:error
```

`Directory.Build.props` に `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` を置き、
IDE0007（`var` ではなく明示的な型を使う）/ IDE0008 を**ビルド時に**評価させる。
**`EnforceCodeStyleInBuild` を書かないと、IDE0007 は Visual Studio の中でしか出ない。**
`dotnet build` と CI では素通りする。この 1 行が無いと規約が効かない。

### 2. `dynamic` を MSBuild のターゲットで検出して失敗させる

`dynamic` は言語機能であり、IDE アナライザの設定では止められない。
`Directory.Build.targets` に、コンパイル対象の `.cs` を正規表現で検査して
見つかったらビルドを失敗させるターゲットを置く。

```
検査パターン: \bdynamic\s+(?!\s)          （型として使われている箇所）
除外: 文字列リテラル・コメントの誤検出は許容し、必要なら該当行に
      // NUTLENS-ALLOW-DYNAMIC を付けて明示的に除外する
```

**誤検出を許容する方向に倒す。** 禁止したいものを見逃すより、
たまに手で除外を書く方が安い。

### 3. 匿名型を使わず、名前付き `record` を定義する

LINQ の射影でも `select new FrameSummary(index, elapsed)` のように名前付きの型を作る。

### 4. Nullable と警告のエラー化

```xml
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<WarningLevel>9999</WarningLevel>
```

`null` の扱いが型で表れるようにする（`string?` と `string` を区別する）。

### 5. WinRT / COM 相互運用も `dynamic` を使わずに書く

`IGraphicsCaptureItemInterop`（ADR-0003）のような COM インタフェースは、
`[ComImport]` / `[Guid]` / `[InterfaceType]` 付きのインタフェース宣言を自分で書き、
`Marshal.GetObjectForIUnknown` の戻りを明示的にキャストする。
**作例の多くが `dynamic` や `object` + リフレクションで書かれているが、採らない。**

### 6. 例外を 1 つだけ設ける：`global using` は使ってよい

型名を省略するものではなく、名前空間の import をまとめるだけなので、
「型が読めなくなる」という本決定の目的に反しない。

## 影響

- コードは冗長になる。`Dictionary<SessionId, List<EncodedFrame>> map = new();`
  のように左辺に完全な型名が並ぶ。**ターゲット型 `new()` は型名が左辺にあるので使ってよい**
- 既存の作例をコピーしてくるとビルドが落ちる。落ちた時点で型を書き起こす作業が毎回入る
- 誤検出された `dynamic` の文字列（例：コメント中の「dynamic」）は手で除外する必要がある
- テストコードにも同じ規約が効く（`Directory.Build.props` はリポジトリ全体に効く）

## 実装で分かったこと（2026-11-13）

### 1. 両方とも実際にビルドを止めた

わざと違反するファイルを置いてビルドし、止まることを確認した。

```
// var
D:\…\_GuardProbe.cs(8,9): error IDE0008: var' ではなく明示的な型を使用します

// dynamic
D:\…\Directory.Build.targets(86,5): error NUTLENS0001: `dynamic` の使用は禁止されています（ADR-0010）。
  … 検出箇所: D:\…\_GuardProbe.cs(8);
```

確認後にファイルを消し、`dotnet build NutLens.slnx` が 0 警告 0 エラーで通ることを確認した。

### 2. `obj/` と生成コードの除外は最初から必要だった

CsWin32 はソースジェネレータなので `@(Compile)` には現れないが、
`obj/` 配下の `AssemblyInfo.cs` などは入る。
検査タスクで `\obj\` と `.g.cs` / `.g.i.cs` / `.Designer.cs` を除外している。
除外しないと、生成コードの都合でビルドが落ちる。

### 3. 相互運用も `dynamic` 無しで書けた

`IGraphicsCaptureItemInterop` / `IDirect3DDxgiInterfaceAccess` を `[ComImport]` で宣言し、
`RoGetActivationFactory` → `Marshal.GetObjectForIUnknown` → パターンマッチで受けた。
作例でよく見る `dynamic` もリフレクションも使っていない。1 インタフェースあたり 10 行程度だった。

### 4. `this.` の強制で地の文が増えた

`.editorconfig` の `dotnet_style_qualification_for_*` を warning にし、
`TreatWarningsAsErrors` と組み合わせているので、実質エラーになる。
意図どおりだが、**この規約を足したことは ADR の決定には書いていなかった**。ここに記録しておく。

## 宿題

（なし。すべて実測で確認済み）

## 却下した案

| 案 | 却下の理由 |
|---|---|
| 規約を `CLAUDE.md` に書くだけにする | **守られているかどうかが人の注意力に依存する。** レビューで毎回指摘することになり、指摘漏れが蓄積する。機械で止められるものを人に任せない |
| `.editorconfig` の重大度を `warning` にする | 警告は増えると読まれなくなる。`TreatWarningsAsErrors` と併用すれば実質エラーだが、**重大度を 2 箇所で決めることになり、片方を緩めたときにもう片方が効いていることに気づけない** |
| `var` を一部許す（`new` の右辺に型名が見えている場合だけ等） | 「型が見えているか」の判断が人によって割れる。規約の線が引けないものは規約にならない。**全面禁止は少なくとも線が明確** |
| StyleCop.Analyzers を入れて規約全体を管理する | 数百のルールが一度に有効になり、本来の目的（`var` と `dynamic` の禁止）と関係ない指摘への対応が先に来る。依存も 1 つ増える。必要なルールは `.editorconfig` だけで表現できる |
| Roslyn アナライザを自作して `dynamic` を検出する | アナライザプロジェクト 1 つと、その単体テストを抱えることになる。**本体の実装より先にアナライザの保守が始まる。** 正規表現検査で目的は足りる |
| `dynamic` の禁止を諦め、WinRT 相互運用でだけ使う | 相互運用こそ型の取り違えが**実行時まで出ない**場所で、最も禁止すべき箇所。`ComImport` 宣言を書く手間は 1 インタフェースあたり 20 行程度 |
