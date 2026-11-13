using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NutLens.App.Mvvm;

/// <summary>
/// 変更通知の最小実装。
/// MVVM のためだけにライブラリを足さない（依存を増やさない方針。CLAUDE.md §4.4）。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        this.RaisePropertyChanged(propertyName);
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>同期のコマンド。</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action execute;
    private readonly Func<bool>? canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => this.canExecute is null || this.canExecute();

    public void Execute(object? parameter) => this.execute();

    public void RaiseCanExecuteChanged() => this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>引数つきの同期コマンド。参考 URL を開くときなどに使う。</summary>
public sealed class RelayCommand<T> : ICommand
    where T : class
{
    private readonly Action<T?> execute;

    public RelayCommand(Action<T?> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        this.execute = execute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => this.execute(parameter as T);

    public void RaiseCanExecuteChanged() => this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 非同期のコマンド。実行中は再実行できない。
/// 撮影は長時間走るので、二重起動を防ぐのはここの責務にする。
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> execute;
    private readonly Func<bool>? canExecute;
    private bool running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 実行中に出た例外。
    /// ICommand.Execute は void なので、受け止めないと未処理例外でアプリごと落ちる。
    /// </summary>
    public event EventHandler<Exception>? ExecutionFailed;

    public bool CanExecute(object? parameter) =>
        !this.running && (this.canExecute is null || this.canExecute());

    public async void Execute(object? parameter)
    {
        if (!this.CanExecute(parameter))
        {
            return;
        }

        this.running = true;
        this.RaiseCanExecuteChanged();
        try
        {
            await this.execute().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            this.ExecutionFailed?.Invoke(this, exception);
        }
        finally
        {
            this.running = false;
            this.RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
