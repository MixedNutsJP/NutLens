using System;
using System.Threading;
using NutLens.App.Resources;
using NutLens.App.ViewModels;
using NutLens.Core.Diagnostics;

namespace NutLens.App.Services;

/// <summary>
/// 例外を利用者向けの文言に直す（6 章 / ADR-0026）。
///
/// <b>変換はここだけ。</b> ViewModel に switch を書かない。
/// 画面が増えるほど、同じ例外が画面ごとに違う文言になり、
/// 利用者は別の不具合だと思う。
///
/// 対応表は詳細設計 06 §7.2。<b>例外を足したら表に 1 行足すまでが 1 つの作業。</b>
/// </summary>
public sealed class ExceptionMessageMapper : IExceptionDescriber
{
    public string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            CaptureUnavailableException => Strings.ErrorCaptureUnavailable,
            CaptureTargetLostException => Strings.ErrorTargetLost,
            CaptureDeviceException => Strings.ErrorCaptureDevice,

            // 探した場所を全部出す。どこを見たか分からないと、設定の直しようがない。
            ClaudeNotFoundException notFound => Strings.ErrorClaudeNotFoundFormat(
                string.Join(Environment.NewLine, notFound.SearchedPaths)),

            ClaudeExecutionException => Strings.ErrorClaudeExecution,
            AnalyzerTemplateException => Strings.ErrorAnalyzerTemplate,

            // 退避先を出す。壊れたファイルは消していないので、利用者が中身を確かめられる。
            SettingsCorruptedException corrupted =>
                Strings.ErrorSettingsCorruptedFormat(corrupted.BackupPath),

            OperationCanceledException => Strings.AnalysisCancelled,

            // 知らない例外を握りつぶさない。型名を出すのは利用者に理解させるためではなく、
            // **不具合報告に貼れるようにする**ため（ADR-0026 決定 3）。
            _ => Strings.ErrorUnexpectedFormat($"{exception.GetType().Name}: {exception.Message}"),
        };
    }
}
