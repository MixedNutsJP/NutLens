using System;
using System.Threading.Tasks;
using NutLens.Core.Capture;
using NutLens.Core.Sessions;

namespace NutLens.App.ViewModels;

/// <summary>
/// 利用者への通知。ViewModel が MessageBox を直に呼ばないようにするための窓口。
/// テストでは偽物を差し込める。
/// </summary>
public interface IUserNotifier
{
    void ShowError(string message);

    /// <summary>警告ではない通知（「保存しました」など）。</summary>
    void ShowInformation(string message);
}

/// <summary>
/// 例外を利用者向けの文言に直す役（6 章 / ADR-0026）。
/// <b>ViewModel は switch を持たない。</b>
/// </summary>
public interface IExceptionDescriber
{
    string Describe(Exception exception);
}

/// <summary>はい／いいえを尋ねる役。既定は「いいえ」側に倒す。</summary>
public interface IConfirmation
{
    /// <summary>「はい」なら true。</summary>
    bool Ask(string message, string title);

    /// <summary>
    /// はい／いいえ／キャンセルを尋ねる。キャンセルなら null。
    /// 「やる / やらない / そもそも止める」の 3 つが要る場面で使う。
    /// </summary>
    bool? AskThreeWay(string message, string title);
}

/// <summary>
/// フォルダを選ばせる役。
/// ViewModel がダイアログを直に開かないための窓口（ADR-0002 決定 4）。
/// </summary>
public interface IFolderPicker
{
    /// <summary>取り消されたら null。</summary>
    string? Pick(string title, string? initialDirectory);
}

/// <summary>
/// Web Detection の有効化に要る問いかけ（5.6 / ADR-0021）。
///
/// <b>同意ダイアログはここだけ。</b>
/// ほかの経路から <c>Enabled</c> を立てられるようにすると、同意ダイアログが飾りになる。
/// </summary>
public interface IWebDetectionPrompt
{
    /// <summary>
    /// 5.6 の 3 点を説明して同意を得る。同意したら true。
    /// <b>同意しなかったら、画面のチェックも元に戻す。</b>
    /// </summary>
    bool AskConsent(int maxImages);

    /// <summary>API キーを入力させる。取り消したら null。</summary>
    string? AskApiKey();
}

/// <summary>
/// 画面上の矩形を選ばせる役。
/// 選ばれた矩形は「モニタ＋モニタ相対の矩形」として返す（ADR-0003 決定 2）。
/// 取り消されたら null。
/// </summary>
public interface IRegionSelector
{
    CaptureTarget? SelectRegion();
}

/// <summary>
/// 撮影の後に解析の画面を開く役。
/// ViewModel がウィンドウを直に扱わないための窓口。
/// </summary>
public interface IAnalysisLauncher
{
    Task LaunchAsync(SessionLayout layout);
}
