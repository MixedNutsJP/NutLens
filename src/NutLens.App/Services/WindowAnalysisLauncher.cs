using System;
using System.Threading.Tasks;
using System.Windows;
using NutLens.App.ViewModels;
using NutLens.Core.Sessions;

namespace NutLens.App.Services;

/// <summary>
/// ViewModel からの「解析を始めて」を、所有ウィンドウつきの画面遷移に変える（段階 2）。
/// ViewModel が <see cref="Window"/> を知らずに済むようにするための薄い層。
/// </summary>
public sealed class WindowAnalysisLauncher : IAnalysisLauncher
{
    private readonly IAnalysisWorkflow workflow;

    public WindowAnalysisLauncher(IAnalysisWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        this.workflow = workflow;
    }

    public Task LaunchAsync(SessionLayout layout) =>
        this.workflow.RunAsync(layout, Application.Current.MainWindow);
}
