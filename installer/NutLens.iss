; NutLens のインストーラ（ADR-0035 / ADR-0036）。
;
; **ここは入口の画面だけを持つ。** インストールの実体・更新・アンインストールは
; すべて Velopack が持つ（ADR-0027）。
; Inno が足すのは「インストール先を GUI で選べること」1 点だけ。
;
; 呼び方:
;   ISCC.exe /DAppVersion=0.1.0 /DVelopackSetup=<Velopack の Setup.exe> /DOutputDir=<出力先> NutLens.iss
;
; 手で叩くこともできるが、**版と入力元を渡し忘れると古いものが混ざる**ので
; 通常は scripts/build-installer.ps1 を使うこと。

#ifndef AppVersion
  #error AppVersion を /DAppVersion=… で渡すこと
#endif
#ifndef VelopackSetup
  #error VelopackSetup を /DVelopackSetup=… で渡すこと（vpk pack が出した Setup.exe）
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; **この GUID は永久に変えない。** 変えると Windows から見て別のアプリになる。
AppId={{3B3321D2-07C4-44F2-950F-76D2F832FEBE}
AppName=NutLens
AppVersion={#AppVersion}
AppVerName=NutLens {#AppVersion}
AppPublisher=MixedNuts
AppPublisherURL=https://github.com/MixedNutsJP/NutLens
AppSupportURL=https://github.com/MixedNutsJP/NutLens/issues

; 既定は Velopack と同じ場所（ADR-0035 決定 2）。
DefaultDirName={localappdata}\NutLensApp
DefaultGroupName=NutLens

; **ディレクトリ選択ページを出すために CreateAppDir=yes が要る。**
; no にすると、このインストーラの存在理由そのものが無くなる。
CreateAppDir=yes
DisableProgramGroupPage=yes

; **アンインストールの口を 2 つ作らない**（ADR-0035 決定 2）。
; 消すのは Velopack 側だけ。ここに uninstaller を作ると
; 「アプリと機能」に 2 つ並び、どちらで消せばよいか分からなくなる。
Uninstallable=no

; UAC を出さない。更新も昇格なしで通る（Velopack と同じ方針）。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir={#OutputDir}
OutputBaseFilename=NutLens-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; 利用者が目にするのは「NutLens を入れる」ことだけ。
; 中で Velopack が動くことは、ここでは説明しない（知る必要が無い）。
japanese.WelcomeLabel2=NutLens {#AppVersion} をこのコンピューターにインストールします。%n%n続行する前に、ほかのアプリケーションをすべて終了してください。

[Files]
; Velopack の Setup.exe。**インストール先には置かない。**
; 展開するのは一時フォルダだけで、役目が終われば消える。
Source: "{#VelopackSetup}"; DestDir: "{tmp}"; DestName: "VelopackSetup.exe"; Flags: deleteafterinstall

[Run]
; **アプリを起こすのはここだけ**（ADR-0036）。
;
; postinstall ＋ nowait なので、利用者が「完了」を押した瞬間に
; **Inno が閉じるのと同時に起動する。** ウィザードの最中には出ない。
; チェックを外せば起動しない。
;
; ルート直下の起動用スタブ（Velopack の extract_stubs_to_dir が置く）を使う。
; current\ の中身は更新のたびに入れ替わるので、そちらを直に指さない。
Filename: "{code:MainExePath}"; Description: "{cm:LaunchProgram,NutLens}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: MainExeExists

[Code]

{
  選ばれたフォルダが空でなければ警告する（ADR-0035 決定 3）。

  **Velopack のアンインストールはルートの中身を選別せずに全部消す**
  （uninstall.rs の remove_dir_contents）。
  D:\Apps のような共用フォルダを選ばれると、アンインストールで
  そこにある他人のファイルごと消えることになる。

  止めはしない。意図して選ぶ場合もある。**黙って通さない**だけ。
}
function DirectoryHasContents(Dir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(Dir) then
    Exit;

  if FindFirst(AddBackslash(Dir) + '*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (CurPageID = wpSelectDir) and DirectoryHasContents(WizardDirValue) then
  begin
    Result := MsgBox(
      '選んだフォルダには、すでにファイルがあります。' + #13#10 + #13#10 +
      WizardDirValue + #13#10 + #13#10 +
      'NutLens をアンインストールすると、このフォルダの中身は' + #13#10 +
      'すべて削除されます。NutLens 専用の空のフォルダを選ぶことをお勧めします。' + #13#10 + #13#10 +
      'このまま続行しますか？',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
end;

{
  起こす実体を決める（ADR-0036）。

  Velopack はルート直下に起動用のスタブを置く（extract_stubs_to_dir）。
  **スタブを使う。** current\ の中身は更新のたびに入れ替わるので、
  そちらを直に指すと更新後にリンクが切れる。
}
function MainExePath(Param: String): String;
begin
  Result := ExpandConstant('{app}\NutLens.exe');
  if not FileExists(Result) then
    Result := ExpandConstant('{app}\current\NutLens.exe');
end;

function MainExeExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\NutLens.exe'))
         or FileExists(ExpandConstant('{app}\current\NutLens.exe'));
end;

{
  Velopack に渡す（ADR-0035 決定 1 / ADR-0036）。

  **--silent で呼ぶ。** これが無いと Velopack がインストール直後にアプリを起動し、
  **ウィザードが終わる前に初期設定画面が出てしまう**（利用者からの指摘）。

  Velopack のソース（install.rs）:
      if !dialogs::get_silent() then start_package(...)
      ※ 原文は Rust だが、**Pascal のコメントは中括弧で閉じる**ので
        ここでは中括弧を外して書いている。

  つまり --silent は「ダイアログを出さない」だけでなく
  **「アプリを起動しない」も兼ねている。**

  進捗は Inno 側が出す。終了コードを見て、失敗なら報せる。
  アプリを起こすのは [Run] の段（利用者がチェックを外せる）。
}
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  SetupPath: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  SetupPath := ExpandConstant('{tmp}\VelopackSetup.exe');

  if not Exec(SetupPath, '--silent --installto "' + ExpandConstant('{app}') + '"',
              '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('インストーラーを起動できませんでした。' + #13#10 + #13#10 +
           SysErrorMessage(ResultCode), mbCriticalError, MB_OK);
    Abort;
  end;

  if ResultCode <> 0 then
  begin
    MsgBox('インストールに失敗しました（終了コード ' + IntToStr(ResultCode) + '）。' + #13#10 + #13#10 +
           '詳しくは %LOCALAPPDATA%\Velopack\ のログを確認してください。',
           mbCriticalError, MB_OK);
    Abort;
  end;
end;
