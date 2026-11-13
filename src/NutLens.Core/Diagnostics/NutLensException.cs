using System;
using System.Collections.Generic;

namespace NutLens.Core.Diagnostics;

/// <summary>
/// NutLens が自分で投げる例外の基底。
/// UI 層はこの型階層を見てメッセージへ変換する（概要設計 §10）。
/// </summary>
public abstract class NutLensException : Exception
{
    protected NutLensException(string message)
        : base(message)
    {
    }

    protected NutLensException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>キャプチャ対象のウィンドウ・モニタが撮影中に失われた。</summary>
public sealed class CaptureTargetLostException : NutLensException
{
    public CaptureTargetLostException(string message)
        : base(message)
    {
    }

    public CaptureTargetLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Direct3D デバイスの作成に失敗した、または撮影中に失われた。</summary>
public sealed class CaptureDeviceException : NutLensException
{
    public CaptureDeviceException(string message)
        : base(message)
    {
    }

    public CaptureDeviceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>この環境では Windows.Graphics.Capture が使えない。</summary>
public sealed class CaptureUnavailableException : NutLensException
{
    public CaptureUnavailableException(string message)
        : base(message)
    {
    }

    public CaptureUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>claude コマンドが見つからない、または --version が失敗した（5.4 / D-05）。</summary>
public sealed class ClaudeNotFoundException : NutLensException
{
    public ClaudeNotFoundException(string message, IReadOnlyList<string> searchedPaths)
        : base(message)
    {
        this.SearchedPaths = searchedPaths;
    }

    /// <summary>探した場所。利用者に「どこを見たか」を示せないと、設定の直しようがない。</summary>
    public IReadOnlyList<string> SearchedPaths { get; }
}

/// <summary>claude の起動そのものに失敗した。</summary>
public sealed class ClaudeExecutionException : NutLensException
{
    public ClaudeExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ClaudeExecutionException(string message)
        : base(message)
    {
    }
}

/// <summary>analyzer/ が見つからない、または中身が足りない（F-23 / ADR-0014）。</summary>
public sealed class AnalyzerTemplateException : NutLensException
{
    public AnalyzerTemplateException(string message)
        : base(message)
    {
    }

    public AnalyzerTemplateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>設定ファイルが壊れていて読めなかった。既定値で続行したうえで通知するために使う。</summary>
public sealed class SettingsCorruptedException : NutLensException
{
    public SettingsCorruptedException(string message, string backupPath)
        : base(message)
    {
        this.BackupPath = backupPath;
    }

    public SettingsCorruptedException(string message, string backupPath, Exception innerException)
        : base(message, innerException)
    {
        this.BackupPath = backupPath;
    }

    /// <summary>壊れたファイルを退避した先。利用者に場所を伝えるために持つ。</summary>
    public string BackupPath { get; }
}
