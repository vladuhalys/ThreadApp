using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace ThreadApp;

public enum WorkloadType
{
    CpuIntensive,
    IOIntensive,
    MixedWorkload,
    BurstProcessing
}

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private ObservableCollection<WorkerThreadViewModel> _threads;
    private int _nextThreadId = 1;
    private readonly DispatcherTimer _updateTimer;
    private int _activeThreadCount = 0;
    private int _totalThreadCount = 0;
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _memCounter;
    private static readonly CountdownEvent _syncEvent = new CountdownEvent(1); // Initially set to 1 to prevent auto-triggering

    public MainWindow()
    {
        InitializeComponent();
        _threads = new ObservableCollection<WorkerThreadViewModel>();
        ThreadListView.ItemsSource = _threads;

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _memCounter = new PerformanceCounter("Memory", "Available MBytes");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize performance counters: {ex.Message}\nSome monitoring features will be disabled.", 
                "Performance Monitoring Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Set up timer to update system information
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();
    }

    private void UpdateTimer_Tick(object sender, EventArgs e)
    {
        UpdateSystemInfo();
        UpdateThreadStatistics();
    }

    private void UpdateSystemInfo()
    {
        try
        {
            if (_cpuCounter != null)
            {
                float cpuUsage = _cpuCounter.NextValue();
                CpuUsageBar.Value = cpuUsage;
                CpuUsageText.Text = $"{cpuUsage:F1}%";
            }

            if (_memCounter != null)
            {
                float memAvailable = _memCounter.NextValue();
                long totalMem = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
                float memUsed = 100 - (memAvailable / totalMem * 100);
                MemoryUsageBar.Value = memUsed;
                MemoryUsageText.Text = $"{memAvailable:F0} MB Available";
            }
        }
        catch (Exception ex)
        {
            // Just log the error but don't crash the app - performance counters can be finicky
            Debug.WriteLine($"Error updating system info: {ex.Message}");
        }
    }

    private void UpdateThreadStatistics()
    {
        _activeThreadCount = 0;
        foreach (var thread in _threads)
        {
            if (thread.Status == "Running" || thread.Status == "Paused")
            {
                _activeThreadCount++;
            }
        }
        _totalThreadCount = _threads.Count;
        ThreadCountText.Text = $"Active: {_activeThreadCount} / Total: {_totalThreadCount}";
    }

    private void AddThreadButton_Click(object sender, RoutedEventArgs e)
    {
        string name = ThreadNameTextBox.Text;
        if (string.IsNullOrEmpty(name))
        {
            name = $"Worker {_nextThreadId}";
        }

        ThreadPriority priority = GetSelectedThreadPriority();
        WorkloadType workload = GetSelectedWorkloadType();
        
        var threadViewModel = new WorkerThreadViewModel(_nextThreadId++, name, priority, workload);
        _threads.Add(threadViewModel);
        UpdateThreadStatistics();
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

    private WorkloadType GetSelectedWorkloadType()
    {
        return ThreadWorkloadComboBox.SelectedIndex switch
        {
            0 => WorkloadType.CpuIntensive,
            1 => WorkloadType.IOIntensive,
            2 => WorkloadType.MixedWorkload,
            3 => WorkloadType.BurstProcessing,
            _ => WorkloadType.CpuIntensive
        };
    }

    private void StartThread_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int threadId)
        {
            var thread = GetThreadById(threadId);
            thread?.Start();
            UpdateThreadStatistics();
        }
    }

    private void PauseThread_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int threadId)
        {
            var thread = GetThreadById(threadId);
            thread?.Pause();
            UpdateThreadStatistics();
        }
    }

    private void ResumeThread_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int threadId)
        {
            var thread = GetThreadById(threadId);
            thread?.Resume();
            UpdateThreadStatistics();
        }
    }

    private void StopThread_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int threadId)
        {
            var thread = GetThreadById(threadId);
            thread?.Stop();
            UpdateThreadStatistics();
        }
    }

    private void StartAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var thread in _threads)
        {
            thread.Start();
        }
        UpdateThreadStatistics();
    }

    private void PauseAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var thread in _threads)
        {
            thread.Pause();
        }
        UpdateThreadStatistics();
    }

    private void ResumeAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var thread in _threads)
        {
            thread.Resume();
        }
        UpdateThreadStatistics();
    }

    private void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var thread in _threads)
        {
            thread.Stop();
        }
        UpdateThreadStatistics();
    }

    private void SynchronizeButton_Click(object sender, RoutedEventArgs e)
    {
        // Reset the countdown event with the current number of running threads
        int runningThreads = 0;
        foreach (var thread in _threads)
        {
            if (thread.Status == "Running" || thread.Status == "Ready")
            {
                runningThreads++;
            }
        }

        if (runningThreads == 0)
        {
            MessageBox.Show("No running threads to synchronize.", "Synchronization", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Reset and initialize the countdown event
        _syncEvent.Reset();
        if (runningThreads > 0)
        {
            _syncEvent.AddCount(runningThreads - 1); // -1 because it was initialized with 1
        }

        // Tell all running threads to synchronize
        foreach (var thread in _threads)
        {
            if (thread.Status == "Running" || thread.Status == "Ready")
            {
                thread.WaitForSynchronization(_syncEvent);
            }
        }

        // Signal to release all threads
        _syncEvent.Signal();

        MessageBox.Show($"Synchronized {runningThreads} threads", "Synchronization", 
            MessageBoxButton.OK, MessageBoxImage.Information);
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
        
        // Dispose performance counters
        _cpuCounter?.Dispose();
        _memCounter?.Dispose();
        
        // Stop the timer
        _updateTimer.Stop();
        
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
    private WorkloadType _workloadType;
    private double _cpuUsage;
    private readonly Stopwatch _cpuStopwatch = new Stopwatch();
    private readonly byte[] _workBuffer = new byte[1024 * 1024]; // 1MB buffer for IO operations
    private Timer _cpuMonitorTimer;
    private readonly Stopwatch _workloadTimer = new Stopwatch();
    private readonly ManualResetEventSlim _syncBarrier = new ManualResetEventSlim(false);
    private readonly string _tempFilePath;

    public int Id { get; }
    public string Name { get; }
    public string Priority => _thread?.Priority.ToString() ?? "Not Set";

    public double CpuUsage
    {
        get => _cpuUsage;
        private set
        {
            if (Math.Abs(_cpuUsage - value) > 0.1) // Only update if changed significantly
            {
                _cpuUsage = value;
                OnPropertyChanged();
            }
        }
    }

    public WorkloadType WorkloadType
    {
        get => _workloadType;
        private set
        {
            if (_workloadType != value)
            {
                _workloadType = value;
                OnPropertyChanged();
            }
        }
    }

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

    public WorkerThreadViewModel(int id, string name, ThreadPriority priority, WorkloadType workloadType)
    {
        Id = id;
        Name = name;
        WorkloadType = workloadType;
        
        // Create temp file path for IO operations
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"ThreadApp_{id}_{Guid.NewGuid()}.tmp");
        
        // Don't start the thread immediately, just configure it
        _thread = new Thread(WorkerMethod)
        {
            IsBackground = true,
            Name = name,
            Priority = priority
        };
        
        // Initialize CPU monitoring
        _cpuMonitorTimer = new Timer(UpdateCpuUsage, null, Timeout.Infinite, 500);
    }

    private void UpdateCpuUsage(object state)
    {
        if (!_isRunning) return;
        
        // This is a simplistic CPU usage estimation based on work done
        double cpuEstimate = 0;
        
        if (_workloadTimer.ElapsedMilliseconds > 0)
        {
            // Calculate a normalized value based on thread type and priority
            switch (WorkloadType)
            {
                case WorkloadType.CpuIntensive:
                    cpuEstimate = 5.0 + _random.NextDouble() * 20.0;
                    break;
                case WorkloadType.IOIntensive:
                    cpuEstimate = 1.0 + _random.NextDouble() * 5.0;
                    break;
                case WorkloadType.MixedWorkload:
                    cpuEstimate = 2.0 + _random.NextDouble() * 12.0;
                    break;
                case WorkloadType.BurstProcessing:
                    cpuEstimate = _progress % 20 < 5 
                        ? 25.0 + _random.NextDouble() * 15.0   // Burst phase
                        : 1.0 + _random.NextDouble() * 3.0;    // Rest phase
                    break;
            }
            
            // Adjust based on priority
            cpuEstimate *= _thread.Priority switch
            {
                ThreadPriority.Highest => 1.5,
                ThreadPriority.AboveNormal => 1.2,
                ThreadPriority.Normal => 1.0,
                ThreadPriority.BelowNormal => 0.8,
                ThreadPriority.Lowest => 0.6,
                _ => 1.0
            };
            
            // Cap to reasonable range
            cpuEstimate = Math.Min(Math.Max(cpuEstimate, 0.1), 35.0);
        }
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            CpuUsage = cpuEstimate;
        });
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
                _syncBarrier.Reset();
                
                // Get the priority safely before creating a new thread
                ThreadPriority priority = ThreadPriority.Normal;
                try
                {
                    // Only try to get the priority if the thread exists and might still be accessible
                    if (_thread != null)
                    {
                        priority = _thread.Priority;
                    }
                }
                catch
                {
                    // If accessing priority fails, use the default priority
                    priority = ThreadPriority.Normal;
                }
                
                // Create and start a new thread with safely retrieved priority
                _thread = new Thread(WorkerMethod)
                {
                    IsBackground = true,
                    Name = Name,
                    Priority = priority
                };
                _thread.Start();
                _isRunning = true;
                
                // Start CPU monitoring and the workload timer
                _workloadTimer.Restart();
                _cpuMonitorTimer.Change(0, 500);
                
                // Update status on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Status = "Running";
                    Progress = 0;
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
                _workloadTimer.Stop();
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
                _workloadTimer.Start();
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
                _syncBarrier.Set(); // In case waiting for synchronization
                _isRunning = false;
                _workloadTimer.Stop();
                _cpuMonitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Status = "Stopped";
                    Progress = 0;
                    CpuUsage = 0;
                });
                
                // Clean up temp file if it exists
                try
                {
                    if (File.Exists(_tempFilePath))
                    {
                        File.Delete(_tempFilePath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cleaning up temp file: {ex.Message}");
                }
            }
        }
    }

    public void WaitForSynchronization(CountdownEvent syncEvent)
    {
        lock (_lockObject)
        {
            if (_thread != null && _thread.IsAlive && _isRunning)
            {
                // Signal that we're ready for the barrier
                syncEvent.Signal();
                
                // Update status
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Status = "Synchronizing";
                });
                
                // After the main thread releases the barrier, we'll continue
                _syncBarrier.Set();
            }
        }
    }

    private void WorkerMethod()
    {
        try
        {
            // Initialize workload-specific resources
            if (WorkloadType == WorkloadType.IOIntensive || WorkloadType == WorkloadType.MixedWorkload)
            {
                // Create a file for IO operations
                using (var fs = new FileStream(_tempFilePath, FileMode.Create))
                {
                    // Pre-allocate some data
                    _random.NextBytes(_workBuffer);
                    fs.Write(_workBuffer, 0, _workBuffer.Length);
                }
            }
            
            // Simulate a workload with random progress increases
            while (!_stopEvent.Wait(0))
            {
                _pauseEvent.Wait(); // Block if paused
                if (_stopEvent.Wait(0)) break;
                
                // Peform work based on the workload type
                PerformWorkload();
                
                // Calculate progress increment based on priority and workload type
                int increment = CalculateProgressIncrement();
                
                // Update progress on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Progress = Math.Min(Progress + increment, 100);
                    
                    if (Progress >= 100)
                    {
                        // Task completed
                        Status = "Completed";
                        _isRunning = false;
                        _cpuMonitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        CpuUsage = 0;
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
                _cpuMonitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
                CpuUsage = 0;
            });
        }
        finally
        {
            // Clean up
            try
            {
                if (File.Exists(_tempFilePath))
                {
                    File.Delete(_tempFilePath);
                }
            }
            catch (Exception) { /* Ignore cleanup errors */ }
        }
    }

    private void PerformWorkload()
    {
        // Wait if we're at a synchronization point
        if (!_syncBarrier.IsSet)
        {
            _syncBarrier.Wait();
        }
        
        switch (WorkloadType)
        {
            case WorkloadType.CpuIntensive:
                // CPU-intensive operations: complex math, loops, etc.
                PerformCpuIntensiveWork();
                break;
                
            case WorkloadType.IOIntensive:
                // IO-intensive operations: file read/write, network simulation, etc.
                PerformIoIntensiveWork();
                break;
                
            case WorkloadType.MixedWorkload:
                // Mix of CPU and IO operations
                if (_random.Next(2) == 0)
                    PerformCpuIntensiveWork();
                else
                    PerformIoIntensiveWork();
                break;
                
            case WorkloadType.BurstProcessing:
                // Alternates between high CPU usage and low CPU usage
                PerformBurstProcessingWork();
                break;
        }
    }

    private void PerformCpuIntensiveWork()
    {
        // Simulate CPU-intensive work
        int iterations = _thread.Priority switch
        {
            ThreadPriority.Highest => 800000,
            ThreadPriority.AboveNormal => 600000,
            ThreadPriority.Normal => 400000,
            ThreadPriority.BelowNormal => 200000,
            ThreadPriority.Lowest => 100000,
            _ => 400000
        };
        
        double result = 0;
        for (int i = 0; i < iterations; i++)
        {
            result += Math.Sqrt(i * Math.Sin(i) * Math.Cos(i));
            if (i % 10000 == 0 && _stopEvent.IsSet) break;
        }
        
        // Small sleep to prevent thread from completely locking CPU
        Thread.Sleep(10);
    }

    private void PerformIoIntensiveWork()
    {
        try
        {
            // Simulate IO operations
            if (File.Exists(_tempFilePath))
            {
                // Sometimes read, sometimes write
                if (_random.Next(2) == 0)
                {
                    using (var fs = new FileStream(_tempFilePath, FileMode.Open))
                    {
                        fs.Read(_workBuffer, 0, _random.Next(1024, _workBuffer.Length));
                    }
                }
                else
                {
                    _random.NextBytes(_workBuffer);
                    using (var fs = new FileStream(_tempFilePath, FileMode.Open))
                    {
                        fs.Position = _random.Next(0, (int)Math.Max(1, fs.Length - 1024));
                        fs.Write(_workBuffer, 0, _random.Next(1024, _workBuffer.Length / 2));
                    }
                }
            }
            
            // IO operations typically involve waiting, so simulate that
            Thread.Sleep(_random.Next(50, 150));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"IO operation error: {ex.Message}");
            Thread.Sleep(100); // Even on error, simulate some delay
        }
    }

    private void PerformBurstProcessingWork()
    {
        // Check if we're in a burst phase
        bool isBurstPhase = Progress % 20 < 5;
        
        if (isBurstPhase)
        {
            // High workload during burst
            PerformCpuIntensiveWork();
        }
        else
        {
            // Low workload between bursts
            Thread.Sleep(_random.Next(100, 200));
        }
    }

    private int CalculateProgressIncrement()
    {
        // Base increment based on thread priority
        int increment = _thread.Priority switch
        {
            ThreadPriority.Highest => _random.Next(3, 7),
            ThreadPriority.AboveNormal => _random.Next(2, 5),
            ThreadPriority.Normal => _random.Next(1, 4),
            ThreadPriority.BelowNormal => _random.Next(1, 3),
            ThreadPriority.Lowest => _random.Next(1, 2),
            _ => 1
        };

        // Adjust based on workload type - some types progress faster/slower
        increment += WorkloadType switch
        {
            WorkloadType.CpuIntensive => _random.Next(1, 3),
            WorkloadType.IOIntensive => _random.Next(2, 4),  // IO might be faster in some phases
            WorkloadType.MixedWorkload => _random.Next(1, 2),
            WorkloadType.BurstProcessing => Progress % 20 < 5 ? _random.Next(3, 6) : _random.Next(0, 2),
            _ => 0
        };
        
        return increment;
    }
    
    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    ~WorkerThreadViewModel()
    {
        // Dispose of resources in finalizer
        _cpuMonitorTimer?.Dispose();
        try
        {
            if (File.Exists(_tempFilePath))
            {
                File.Delete(_tempFilePath);
            }
        }
        catch { /* Ignore exceptions in finalizer */ }
    }
}

