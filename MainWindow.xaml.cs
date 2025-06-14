using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ThreadApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private ObservableCollection<WorkerThreadViewModel> _threads;
    private int _nextThreadId = 1;

    public MainWindow()
    {
        InitializeComponent();
        _threads = new ObservableCollection<WorkerThreadViewModel>();
        ThreadListView.ItemsSource = _threads;
    }

    private void AddThreadButton_Click(object sender, RoutedEventArgs e)
    {
        string name = ThreadNameTextBox.Text;
        if (string.IsNullOrEmpty(name))
        {
            name = $"Worker {_nextThreadId}";
        }

        ThreadPriority priority = GetSelectedThreadPriority();
        var threadViewModel = new WorkerThreadViewModel(_nextThreadId++, name, priority);
        _threads.Add(threadViewModel);
    }

    private ThreadPriority GetSelectedThreadPriority()
    {
        return ThreadPriorityComboBox.SelectedIndex switch
        {
            0 => ThreadPriority.Lowest,
            1 => ThreadPriority.BelowNormal,
            2 => ThreadPriority.Normal,
            3 => ThreadPriority.AboveNormal,
            4 => ThreadPriority.Highest,
            _ => ThreadPriority.Normal
        };
    }

    private void StartThread_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int threadId)
        {
            var thread = GetThreadById(threadId);
            thread?.Start();
        }
    }

    private void PauseThread_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int threadId)
        {
            var thread = GetThreadById(threadId);
            thread?.Pause();
        }
    }

    private void ResumeThread_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int threadId)
        {
            var thread = GetThreadById(threadId);
            thread?.Resume();
        }
    }

    private void StopThread_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int threadId)
        {
            var thread = GetThreadById(threadId);
            thread?.Stop();
        }
    }

    private void StartAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var thread in _threads)
        {
            thread.Start();
        }
    }

    private void PauseAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var thread in _threads)
        {
            thread.Pause();
        }
    }

    private void ResumeAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var thread in _threads)
        {
            thread.Resume();
        }
    }

    private void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var thread in _threads)
        {
            thread.Stop();
        }
    }

    private WorkerThreadViewModel GetThreadById(int id)
    {
        foreach (var thread in _threads)
        {
            if (thread.Id == id)
                return thread;
        }
        return null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Cleanup all threads when application closes
        foreach (var thread in _threads)
        {
            thread.Stop();
        }
        base.OnClosing(e);
    }
}

// ViewModel for worker threads with UI-binding properties
public class WorkerThreadViewModel : INotifyPropertyChanged
{
    private readonly Random _random = new Random();
    private Thread _thread;
    private readonly ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);
    private readonly ManualResetEventSlim _stopEvent = new ManualResetEventSlim(false);
    private int _progress;
    private string _status = "Ready";
    private readonly object _lockObject = new object();
    private bool _isRunning;

    public int Id { get; }
    public string Name { get; }
    public string Priority => _thread?.Priority.ToString() ?? "Not Set";

    public int Progress
    {
        get => _progress;
        private set
        {
            if (_progress != value)
            {
                _progress = value;
                OnPropertyChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public WorkerThreadViewModel(int id, string name, ThreadPriority priority)
    {
        Id = id;
        Name = name;
        // Don't start the thread immediately, just configure it
        _thread = new Thread(WorkerMethod)
        {
            IsBackground = true,
            Name = name,
            Priority = priority
        };
    }

    public void Start()
    {
        lock (_lockObject)
        {
            if (_isRunning) return;
            
            // We need to create a new thread if it's not alive or doesn't exist
            if (_thread == null || !_thread.IsAlive)
            {
                _stopEvent.Reset();
                _pauseEvent.Set();
                
                // Create and start a new thread
                _thread = new Thread(WorkerMethod)
                {
                    IsBackground = true,
                    Name = Name,
                    Priority = _thread?.Priority ?? ThreadPriority.Normal
                };
                _thread.Start();
                _isRunning = true;
                
                // Update status on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Status = "Running";
                });
            }
            else
            {
                Resume();
            }
        }
    }

    public void Pause()
    {
        lock (_lockObject)
        {
            if (_isRunning)
            {
                _pauseEvent.Reset();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Status = "Paused";
                });
            }
        }
    }

    public void Resume()
    {
        lock (_lockObject)
        {
            if (_thread != null && _thread.IsAlive)
            {
                _pauseEvent.Set();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Status = "Running";
                });
            }
        }
    }

    public void Stop()
    {
        lock (_lockObject)
        {
            if (_thread != null && _thread.IsAlive)
            {
                _stopEvent.Set();
                _pauseEvent.Set(); // In case thread is paused
                _isRunning = false;
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Status = "Stopped";
                    Progress = 0;
                });
            }
        }
    }

    private void WorkerMethod()
    {
        try
        {
            // Simulate a workload with random progress increases
            while (!_stopEvent.Wait(0))
            {
                _pauseEvent.Wait(); // Block if paused

                if (_stopEvent.Wait(0)) break;

                // Simulate work by sleeping and updating progress
                Thread.Sleep(_random.Next(50, 200));

                // Calculate a random progress increment based on thread priority
                int increment = _thread.Priority switch
                {
                    ThreadPriority.Highest => _random.Next(3, 7),
                    ThreadPriority.AboveNormal => _random.Next(2, 5),
                    ThreadPriority.Normal => _random.Next(1, 4),
                    ThreadPriority.BelowNormal => _random.Next(1, 3),
                    ThreadPriority.Lowest => _random.Next(1, 2),
                    _ => 1
                };

                // Update progress on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Progress = Math.Min(Progress + increment, 100);
                    
                    if (Progress >= 100)
                    {
                        // Task completed
                        Status = "Completed";
                        _isRunning = false;
                    }
                });

                if (Progress >= 100) break;
            }
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Status = $"Error: {ex.Message}";
                _isRunning = false;
            });
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

