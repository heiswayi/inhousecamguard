using AForge;
using AForge.Video;
using AForge.Video.DirectShow;
using AForge.Vision.Motion;
using Ookii.Dialogs.Wpf;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace InhouseCamguard
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region INotifyPropertyChanged members

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                handler(this, e);
            }
        }

        #endregion INotifyPropertyChanged members

        private FilterInfo _currentDevice;
        private PlotModel _oxyGraph;
        private LineSeries blobCountingSeries;
        private DispatcherTimer clockTimer;
        private int detectedObjectsCount = 0;
        private bool displayMotionHistory = false;
        private string datalogFilePath;
        private bool isMotionDetected = false;
        private float motionTriggerThreshold = 0.015f;
        private int motionDetectionType = 1;
        private MotionDetector motionDetector;
        private List<float> motionHistory = new List<float>();
        private float motionLevel = 0;
        private LineSeries motionLevelSeries;
        private int motionProcessingType = 1;
        private int motionSensitivity = 0;
        private DispatcherTimer graphUpdateTimer;
        private DateTime startTime;
        private LineSeries triggerThresholdSeries;
        private IVideoSource videoSource;
        private LinearAxis xAxis;
        private LinearAxis yAxisLeft;
        private LinearAxis yAxisRight;
        private int ensureUniqueCounter = 0;
        private bool canLogData = false;
        private System.Timers.Timer customIntervalTimer;
        private bool customIntervalEnabled = false;
        private MotionLevelData motionLevelData = null;
        private readonly object chartLock = new object();
        private int pointsLimitCounter = 0;
        private BitmapImage bitmapImageCopy;
        private Histogram histogramWindow;
        private const int statLength = 15;
        private int statIndex = 0;
        private int statReady = 0;
        private int[] statCount = new int[statLength];
        private bool playOnce = false;
        private bool enableBeepOnTrigger = false;
        private bool captureImageOnce = false;

        public MainWindow()
        {
            InitializeComponent();
            Title = "Inhouse Camguard - Live Video Motion Detection Analysis & Data Logging";
            DataContext = this;
            Closing += App_Closing;
            SizeChanged += App_SizeChanged;
            Loaded += App_Loaded;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            GetVideoDevices();
            motionDetector = new MotionDetector(
            new TwoFramesDifferenceDetector(),
            new MotionAreaHighlighting());
            OxyGraph = CreatePlotModel();
            graphUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1)
            };
            graphUpdateTimer.Tick += UpdateTimer_Tick;
            StopButton.IsEnabled = false;
            clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            clockTimer.Tick += ClockTimer_Tick;
            startTimeClock.Content = "00:00:00";
            stopTimeClock.Content = "00:00:00";
            currentTimeClock.Content = "00:00:00";

            // file
            tbDirectory.Text = FileHelpers.GetDefaultDirectory();
            tbFilename.Text = FileHelpers.GetDefaultFilename();
            tbSaveImgLocation.Text = FileHelpers.GetDefaultImageDirectory();

            // stop after X seconds default
            tbStopAfterXSeconds.Text = "30";
            // custom logging interval
            tbLoggingInterval.Text = "5";
            // limit points
            tbLimitDataLogPoint.Text = "1000";

            // update necessary ui
            UpdateStatusOnUI();
        }

        #region DllImport
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);
        private void ShowDiskFreeSpace(string path)
        {
            ulong FreeBytesAvailable;
            ulong TotalNumberOfBytes;
            ulong TotalNumberOfFreeBytes;

            bool success = GetDiskFreeSpaceEx(path, out FreeBytesAvailable, out TotalNumberOfBytes, out TotalNumberOfFreeBytes);
            if (success)
            {
                float percent = (float)FreeBytesAvailable / (float)TotalNumberOfBytes * 100f;
                lblFreeSpaceAvailable.Content = FileHelpers.BytesToString((long)FreeBytesAvailable);
                if (percent < 10) lblFreeSpaceAvailable.Foreground = System.Windows.Media.Brushes.Red;
                else lblFreeSpaceAvailable.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                lblFreeSpaceAvailable.Content = "ERR";
                lblFreeSpaceAvailable.Foreground = System.Windows.Media.Brushes.Orange;
            }
        }
        #endregion

        #region Binding properties
        public FilterInfo CurrentDevice
        {
            get { return _currentDevice; }
            set { _currentDevice = value; this.OnPropertyChanged(nameof(CurrentDevice)); }
        }
        public PlotModel OxyGraph
        {
            get { return _oxyGraph; }
            set { _oxyGraph = value; this.OnPropertyChanged(nameof(OxyGraph)); }
        }
        public Collection<MotionLevelData> GraphDataCollection { get; set; }
        public ObservableCollection<FilterInfo> VideoDevices { get; set; }
        public float TriggerThreshold
        {
            get { return motionTriggerThreshold; }
            set { motionTriggerThreshold = value; this.OnPropertyChanged(nameof(TriggerThreshold)); }
        }
        #endregion

        #region Timer events
        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            // running clock
            currentTimeClock.Content = DateTime.Now.ToString("hh:mm:ss");

            // get free space
            ShowDiskFreeSpace(tbSaveImgLocation.Text);

            // when camera started streaming video
            fpsLbl.Content = "-";
            if (videoSource != null && videoSource.IsRunning)
            {
                if (playOnce) playOnce = false;
                if (captureImageOnce) captureImageOnce = false;

                // get number of frames for the last second
                statCount[statIndex] = videoSource.FramesReceived;

                // increment indexes
                if (++statIndex >= statLength)
                    statIndex = 0;
                if (statReady < statLength)
                    statReady++;

                float fps = 0;

                // calculate average value
                for (int i = 0; i < statReady; i++)
                {
                    fps += statCount[i];
                }
                fps /= statReady;

                statCount[statIndex] = 0;

                fpsLbl.Content = fps.ToString("F2") + " fps";

                // update histograms
                OnBitmapUpdated?.Invoke(this, bitmapImageCopy);
            }
        }
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            lock (chartLock)
            {
                if (motionLevel > motionTriggerThreshold)
                {
                    if (!playOnce && enableBeepOnTrigger)
                    {
                        playOnce = true;
                        PlayBeep();
                    }
                    if (!captureImageOnce && cbEnableSaveImg.IsChecked == true)
                    {
                        captureImageOnce = true;
                        CaptureAsImage();
                    }
                }

                // update data into collection
                DateTime currentTime = DateTime.Now;
                double elapsedTime = (currentTime - startTime).TotalSeconds;
                motionLevelData = new MotionLevelData(elapsedTime, motionLevel, motionTriggerThreshold, startTime, currentTime, detectedObjectsCount);
                GraphDataCollection.Add(motionLevelData);
                if (GraphDataCollection.Count > 10000) GraphDataCollection.RemoveAt(0);

                // update sensitivity bar
                UpdateSensivityBarUI();

                // update graph
                OxyGraph.InvalidatePlot(true);

                // autoscroll graph x-axis
                if ((elapsedTime + 0.001) >= xAxis.Maximum)
                {
                    double panStep = (xAxis.ActualMaximum - xAxis.DataMaximum) * xAxis.Scale;
                    xAxis.Pan(panStep);
                }

                // write to log file
                if (!customIntervalEnabled) LogData(motionLevelData);
            }
        }
        private void CustomIntervalTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            LogData(motionLevelData);
        }
        #endregion

        #region OxyPlot components
        // Only for static chart!
        private void AdjustYExtent(LineSeries lserie, LinearAxis xaxis, LinearAxis yaxis)
        {
            if (xaxis != null && yaxis != null && lserie.Points.Count() != 0)
            {
                double istart = xaxis.ActualMinimum;
                double iend = xaxis.ActualMaximum;

                var ptlist = lserie.Points.FindAll(p => p.X >= istart && p.X <= iend);

                double ymin = double.MaxValue;
                double ymax = double.MinValue;
                for (int i = 0; i <= ptlist.Count() - 1; i++)
                {
                    ymin = Math.Min(ymin, ptlist[i].Y);
                    ymax = Math.Max(ymax, ptlist[i].Y);
                }

                var extent = ymax - ymin;
                var margin = extent * 0; //change the 0 by a value to add some extra up and down margin

                yaxis.Zoom(ymin - margin, ymax + margin);
            }
        }
        private PlotModel CreatePlotModel()
        {
            GraphDataCollection = new Collection<MotionLevelData>();
            PlotModel model = new PlotModel();
            xAxis = new LinearAxis()
            {
                Position = AxisPosition.Bottom,
                IsZoomEnabled = false,
                //IsPanEnabled = false,
                Minimum = 0,
                Maximum = 10,
                MajorGridlineStyle = LineStyle.Dash,
                MinorGridlineStyle = LineStyle.Dot,
                Key = "ElapsedTimeKey"
            };
            yAxisLeft = new LinearAxis()
            {
                Position = AxisPosition.Left,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                MajorGridlineStyle = LineStyle.Dash,
                MinorGridlineStyle = LineStyle.Dot,
                Minimum = 0,
                Key = "MotionLevelKey"
            };
            model.Axes.Add(xAxis);
            model.Axes.Add(yAxisLeft);

            return model;
        }
        private void CreateStandardAnalysisSeries()
        {
            motionLevelSeries = new LineSeries()
            {
                Title = "Motion Level",
                ItemsSource = GraphDataCollection,
                DataFieldX = "ElapsedTime",
                DataFieldY = "Value",
                Color = OxyColors.Blue,
                StrokeThickness = 1,
                CanTrackerInterpolatePoints = true,
                Smooth = true,
                XAxisKey = "ElapsedTimeKey",
                YAxisKey = "MotionLevelKey"
            };
            triggerThresholdSeries = new LineSeries()
            {
                Title = "Trigger Threshold",
                ItemsSource = GraphDataCollection,
                DataFieldX = "ElapsedTime",
                DataFieldY = "ThresholdValue",
                Color = OxyColors.Red,
                StrokeThickness = 1,
                CanTrackerInterpolatePoints = true,
                XAxisKey = "ElapsedTimeKey",
                YAxisKey = "MotionLevelKey"
            };
            if (OxyGraph.Series.Contains(motionLevelSeries)) OxyGraph.Series.Remove(motionLevelSeries);
            OxyGraph.Series.Add(motionLevelSeries);
            if (OxyGraph.Series.Contains(triggerThresholdSeries)) OxyGraph.Series.Remove(triggerThresholdSeries);
            OxyGraph.Series.Add(triggerThresholdSeries);
        }
        private void EnableBlobCountAnalysis()
        {
            if (motionProcessingType == 3)
            {
                yAxisRight = new LinearAxis()
                {
                    Position = AxisPosition.Right,
                    IsZoomEnabled = false,
                    IsPanEnabled = false,
                    //MajorGridlineStyle = LineStyle.Dash,
                    //MinorGridlineStyle = LineStyle.Dot,
                    Minimum = 0,
                    Key = "BlobCountKey"
                };
                if (OxyGraph.Axes.Contains(yAxisRight)) OxyGraph.Axes.Remove(yAxisRight);
                OxyGraph.Axes.Add(yAxisRight);

                blobCountingSeries = new LineSeries()
                {
                    Title = "Blob Count",
                    ItemsSource = GraphDataCollection,
                    DataFieldX = "ElapsedTime",
                    DataFieldY = "BlobCount",
                    Color = OxyColors.Green,
                    StrokeThickness = 1,
                    CanTrackerInterpolatePoints = true,
                    XAxisKey = "ElapsedTimeKey",
                    YAxisKey = "BlobCountKey"
                };
                if (OxyGraph.Series.Contains(blobCountingSeries)) OxyGraph.Series.Remove(blobCountingSeries);
                OxyGraph.Series.Add(blobCountingSeries);
            }
            else
            {
                if (OxyGraph.Series.Contains(blobCountingSeries)) OxyGraph.Series.Remove(blobCountingSeries);
                if (OxyGraph.Axes.Contains(yAxisRight)) OxyGraph.Axes.Remove(yAxisRight);
            }
            this.OnPropertyChanged(nameof(OxyGraph));
        }
        #endregion

        #region AForge components
        private void DrawMotionHistory(Bitmap image)
        {
            System.Drawing.Color greenColor = System.Drawing.Color.FromArgb(128, 0, 255, 0);
            System.Drawing.Color yellowColor = System.Drawing.Color.FromArgb(128, 255, 255, 0);
            System.Drawing.Color redColor = System.Drawing.Color.FromArgb(128, 255, 0, 0);

            BitmapData bitmapData = image.LockBits(new System.Drawing.Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.ReadWrite, image.PixelFormat);

            int t1 = (int)(motionTriggerThreshold * 500);
            int t2 = (int)(0.075 * 500);

            for (int i = 1, n = motionHistory.Count; i <= n; i++)
            {
                int motionBarLength = (int)(motionHistory[n - i] * 500);

                if (motionBarLength == 0)
                    continue;

                if (motionBarLength > 50)
                    motionBarLength = 50;

                AForge.Imaging.Drawing.Line(bitmapData,
                    new IntPoint(image.Width - i, image.Height - 1),
                    new IntPoint(image.Width - i, image.Height - 1 - motionBarLength),
                    greenColor);

                if (motionBarLength > t1)
                {
                    AForge.Imaging.Drawing.Line(bitmapData,
                        new IntPoint(image.Width - i, image.Height - 1 - t1),
                        new IntPoint(image.Width - i, image.Height - 1 - motionBarLength),
                        yellowColor);
                }

                if (motionBarLength > t2)
                {
                    AForge.Imaging.Drawing.Line(bitmapData,
                        new IntPoint(image.Width - i, image.Height - 1 - t2),
                        new IntPoint(image.Width - i, image.Height - 1 - motionBarLength),
                        redColor);
                }
            }

            image.UnlockBits(bitmapData);
        }
        private void GetVideoDevices()
        {
            VideoDevices = new ObservableCollection<FilterInfo>();
            foreach (FilterInfo filterInfo in new FilterInfoCollection(FilterCategory.VideoInputDevice))
            {
                VideoDevices.Add(filterInfo);
            }
            if (VideoDevices.Any())
            {
                CurrentDevice = VideoDevices[0];
            }
            else
            {
                MessageBox.Show("No video sources found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void videoSource_NewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs)
        {
            try
            {
                BitmapImage bi;
                using (var bitmap = (Bitmap)eventArgs.Frame.Clone())
                {
                    lock (this)
                    {
                        if (motionDetector != null)
                        {
                            motionLevel = motionDetector.ProcessFrame(bitmap);

                            motionSensitivity = (int)(motionLevel / motionTriggerThreshold * 100);

                            if (motionLevel > motionTriggerThreshold)
                            {
                                isMotionDetected = true;
                            }
                            else
                            {
                                isMotionDetected = false;
                            }

                            // check objects' count
                            if (motionDetector.MotionProcessingAlgorithm is BlobCountingObjectsProcessing)
                            {
                                BlobCountingObjectsProcessing countingDetector = (BlobCountingObjectsProcessing)motionDetector.MotionProcessingAlgorithm;
                                detectedObjectsCount = countingDetector.ObjectsCount;
                            }
                            else
                            {
                                detectedObjectsCount = 0;
                            }

                            motionHistory.Add(motionLevel);
                            if (motionHistory.Count > 300) motionHistory.RemoveAt(0);
                            if (displayMotionHistory) DrawMotionHistory(bitmap);
                        }
                    }

                    bi = bitmap.ToBitmapImage();
                }
                bi.Freeze(); // avoid cross thread operations and prevents leaks
                Dispatcher.BeginInvoke(new ThreadStart(delegate { videoPlayer.Source = bi; }));

                bitmapImageCopy = bi;
            }
            catch (Exception exc)
            {
                DispatchService.Invoke(() =>
                {
                    StopButton_Click(StopButton, new RoutedEventArgs());
                });
                MessageBox.Show("Error on videoSource_NewFrame:\n" + exc.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region App/MainWindow events
        private void App_Closing(object sender, CancelEventArgs e)
        {
            StopCamera();
            histogramWindow?.Close();
            clockTimer.Stop();
        }
        private void App_SizeChanged(object sender, SizeChangedEventArgs e)
        {
        }
        private void App_Loaded(object sender, RoutedEventArgs e)
        {
            clockTimer.Start();
        }
        #endregion

        #region Start/Stop button
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // setup filename
            datalogFilePath = FileHelpers.EnsureUnique(Path.Combine(tbDirectory.Text, tbFilename.Text + FileHelpers.CsvExtension));
            tbFilename.Text = Path.GetFileNameWithoutExtension(datalogFilePath);

            // check if logging is enable
            if (cbEnableLogging.IsChecked == true) canLogData = true;
            else canLogData = false;

            // reset chart and sensivity bar
            OxyGraph = CreatePlotModel();
            CreateStandardAnalysisSeries();
            pbMotionLevel.Value = 0;

            // reset some values
            motionLevel = 0;
            motionHistory.Clear();
            pointsLimitCounter = 0;

            // start camera
            StartCamera();

            // set time and start timers
            startTime = DateTime.Now;
            graphUpdateTimer.Start();

            // stop after X seconds
            if (cbStopAfterXSeconds.IsChecked == true && Convert.ToInt32(tbStopAfterXSeconds.Text) > 0)
            {
                System.Timers.Timer t = new System.Timers.Timer();
                t.Interval = Convert.ToInt32(tbStopAfterXSeconds.Text) * 1000;
                t.Elapsed += (s, o) =>
                {
                    DispatchService.Invoke(() =>
                    {
                        StopButton_Click(StopButton, new RoutedEventArgs());
                    });
                    t.Stop();
                    t.Dispose();
                };
                t.Start();
            }

            // custom logging interval
            if (cbLoggingInterval.IsChecked == true && Convert.ToInt32(tbLoggingInterval.Text) > 0)
            {
                customIntervalTimer = new System.Timers.Timer();
                customIntervalTimer.Interval = Convert.ToInt32(tbLoggingInterval.Text) * 1000;
                customIntervalTimer.Elapsed += CustomIntervalTimer_Elapsed;
                customIntervalTimer.Start();
            }

            // update ui at the end
            startTimeClock.Content = startTime.ToString("hh:mm:ss");
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            mainWindow.ResizeMode = ResizeMode.CanMinimize;
            LockUIControls(true);
            UpdateStatusOnUI();
        }
        private void StartCamera()
        {
            if (CurrentDevice != null)
            {
                videoSource = new VideoCaptureDevice(CurrentDevice.MonikerString);
                videoSource.NewFrame += videoSource_NewFrame;
                videoSource.Start();
            }
        }
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // stop logging if any
            canLogData = false;

            // stop camera
            StopCamera();

            // mark stop time
            string stopTime = DateTime.Now.ToString("hh:mm:ss");

            // stop graph update
            graphUpdateTimer.Stop();

            // stop custom logging interval timer
            if (customIntervalTimer != null && customIntervalTimer.Enabled) customIntervalTimer.Stop();

            // update ui at the end
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            mainWindow.ResizeMode = ResizeMode.CanResize;
            stopTimeClock.Content = stopTime;
            LockUIControls(false);
            UpdateStatusOnUI();
        }
        private void StopCamera()
        {
            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
                videoSource.NewFrame -= new NewFrameEventHandler(videoSource_NewFrame);
            }
        }
        #endregion

        #region Motion Algorithms menu
        private void mdaNone_Click(object sender, RoutedEventArgs e)
        {
            motionDetectionType = 0;
            SetMotionDetectionAlgorithm(null);

            EnableBlobCountAnalysis();
        }
        private void mdaSBM_Click(object sender, RoutedEventArgs e)
        {
            motionDetectionType = 2;
            SetMotionDetectionAlgorithm(new SimpleBackgroundModelingDetector(true, true));

            EnableBlobCountAnalysis();
        }
        private void mdaTFD_Click(object sender, RoutedEventArgs e)
        {
            motionDetectionType = 1;
            SetMotionDetectionAlgorithm(new TwoFramesDifferenceDetector());

            EnableBlobCountAnalysis();
        }
        private void motionMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            MenuItem[] motionDetectionItems = new MenuItem[]
            {
                // must follow these sequence
                mdaNone, mdaTFD, mdaSBM
            };
            MenuItem[] motionProcessingItems = new MenuItem[]
            {
                // must follow these sequence
                mpaNone, mpaMAH, mpaMBH, mpaBCP, mpaGMAP
            };
            for (int i = 0; i < motionDetectionItems.Length; i++)
            {
                motionDetectionItems[i].IsChecked = i == motionDetectionType;
            }
            for (int i = 0; i < motionProcessingItems.Length; i++)
            {
                motionProcessingItems[i].IsChecked = i == motionProcessingType;
            }
            bool enabled = motionDetectionType != 1;
            mpaMBH.IsEnabled = enabled;
            mpaBCP.IsEnabled = enabled;
        }
        private void mpaBCP_Click(object sender, RoutedEventArgs e)
        {
            motionProcessingType = 3;
            SetMotionProcessingAlgorithm(new BlobCountingObjectsProcessing());

            EnableBlobCountAnalysis();
        }
        private void mpaGMAP_Click(object sender, RoutedEventArgs e)
        {
            motionProcessingType = 4;
            SetMotionProcessingAlgorithm(new GridMotionAreaProcessing(32, 32));

            EnableBlobCountAnalysis();
        }
        private void mpaMAH_Click(object sender, RoutedEventArgs e)
        {
            motionProcessingType = 1;
            SetMotionProcessingAlgorithm(new MotionAreaHighlighting());

            EnableBlobCountAnalysis();
        }
        private void mpaMBH_Click(object sender, RoutedEventArgs e)
        {
            motionProcessingType = 2;
            SetMotionProcessingAlgorithm(new MotionBorderHighlighting());

            EnableBlobCountAnalysis();
        }
        private void mpaNone_Click(object sender, RoutedEventArgs e)
        {
            motionProcessingType = 0;
            SetMotionProcessingAlgorithm(null);

            EnableBlobCountAnalysis();
        }
        private void displayMotionHistoryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            displayMotionHistoryMenuItem.IsChecked = !displayMotionHistory;
            displayMotionHistory = displayMotionHistoryMenuItem.IsChecked;
        }
        private void SetMotionDetectionAlgorithm(IMotionDetector detectionAlgorithm)
        {
            lock (this)
            {
                motionDetector.MotionDetectionAlgorithm = detectionAlgorithm;

                if (detectionAlgorithm is TwoFramesDifferenceDetector)
                {
                    if ((motionDetector.MotionProcessingAlgorithm is MotionBorderHighlighting) ||
                        (motionDetector.MotionProcessingAlgorithm is BlobCountingObjectsProcessing))
                    {
                        motionProcessingType = 1;
                        SetMotionProcessingAlgorithm(new MotionAreaHighlighting());
                    }
                }
            }
        }
        private void SetMotionProcessingAlgorithm(IMotionProcessing processingAlgorithm)
        {
            lock (this)
            {
                motionDetector.MotionProcessingAlgorithm = processingAlgorithm;
            }
        }
        #endregion Motion menu

        #region Tools menu
        private void cvSettings_Click(object sender, RoutedEventArgs e)
        {
            if ((videoSource != null) && (videoSource is VideoCaptureDevice) && (videoSource.IsRunning))
            {
                Console.WriteLine("Current input: " + ((VideoCaptureDevice)videoSource).CrossbarVideoInput);

                try
                {
                    ((VideoCaptureDevice)videoSource).DisplayCrossbarPropertyPage(new WindowInteropHelper(this).Handle);
                }
                catch (NotSupportedException ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void lvcSettings_Click(object sender, RoutedEventArgs e)
        {
            if ((videoSource != null) && (videoSource is VideoCaptureDevice))
            {
                try
                {
                    ((VideoCaptureDevice)videoSource).DisplayPropertyPage(new WindowInteropHelper(this).Handle);
                }
                catch (NotSupportedException ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void toolsMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            lvcSettings.IsEnabled =
                ((videoSource != null) && (videoSource is VideoCaptureDevice));
            cvSettings.IsEnabled =
                ((videoSource != null) && (videoSource is VideoCaptureDevice) && (videoSource.IsRunning));
        }
        #endregion Tools menu

        #region File menu
        private void exit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        #endregion File menu

        #region Data logging
        private void LogData(MotionLevelData data)
        {
            if (data == null) return;
            if (cbLimitDataLog.IsChecked == true && pointsLimitCounter > Convert.ToInt32(tbLimitDataLogPoint.Text))
            {
                canLogData = false;
                UpdateStatusOnUI();
                return;
            }
            if (cbLogGreaterThanThreshold.IsChecked == true && data.Value < data.ThresholdValue) return;
            if (canLogData)
            {
                try
                {
                    if (!File.Exists(datalogFilePath))
                    {
                        const string header = "Elapsed Time (s), Motion Level, Trigger Threshold, Blob Count, Start Time, Current Time";
                        File.WriteAllText(datalogFilePath, header + Environment.NewLine);
                    }
                    var sb = new StringBuilder();
                    sb.Append(data.ElapsedTime);
                    sb.Append("," + data.Value);
                    sb.Append("," + data.ThresholdValue);
                    sb.Append("," + data.BlobCount);
                    sb.Append("," + data.StartTime.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                    sb.Append("," + data.CurrentTime.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                    sb.Append(Environment.NewLine);
                    File.AppendAllText(datalogFilePath, sb.ToString());
                    pointsLimitCounter++;
                }
                catch
                {
                    // ignored
                }
            }
        }
        private void btnBrowseDir_Click(object sender, RoutedEventArgs e)
        {
            VistaFolderBrowserDialog dlg = new VistaFolderBrowserDialog();
            dlg.SelectedPath = tbDirectory.Text;
            dlg.ShowNewFolderButton = true;

            if (dlg.ShowDialog() == true)
            {
                tbDirectory.Text = dlg.SelectedPath;
            }
        }
        #endregion

        #region UI components
        private void UpdateStatusOnUI()
        {
            if (canLogData)
            {
                lblLoggingStatus.Content = "True";
                lblLoggingStatus.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                lblLoggingStatus.Content = "False";
                lblLoggingStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
        private void UpdateSensivityBarUI()
        {
            pbMotionLevel.Value = motionSensitivity;
            if (pbMotionLevel.Value > 80) pbMotionLevel.Foreground = System.Windows.Media.Brushes.Red;
            else if (pbMotionLevel.Value > 50) pbMotionLevel.Foreground = System.Windows.Media.Brushes.Orange;
            else pbMotionLevel.Foreground = System.Windows.Media.Brushes.Green;
        }
        private void LockUIControls(bool lockEnabled)
        {
            if (cbEnableLogging.IsChecked == true && lockEnabled)
            {
                tbFilename.IsEnabled = false;
                btnBrowseDir.IsEnabled = false;
                cbStopAfterXSeconds.IsEnabled = false;
                cbLoggingInterval.IsEnabled = false;
                cbLogGreaterThanThreshold.IsEnabled = false;
                cbLimitDataLog.IsEnabled = false;
                tbStopAfterXSeconds.IsEnabled = false;
                tbLoggingInterval.IsEnabled = false;
                tbLimitDataLogPoint.IsEnabled = false;
            }
            else
            {
                tbFilename.IsEnabled = true;
                btnBrowseDir.IsEnabled = true;
                cbStopAfterXSeconds.IsEnabled = true;
                cbLoggingInterval.IsEnabled = true;
                cbLogGreaterThanThreshold.IsEnabled = true;
                cbLimitDataLog.IsEnabled = true;
                tbStopAfterXSeconds.IsEnabled = true;
                tbLoggingInterval.IsEnabled = true;
                tbLimitDataLogPoint.IsEnabled = true;
            }
        }
        #endregion

        #region About dialog
        private void aboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowTaskDialogAbout();
        }
        private void ShowTaskDialogAbout()
        {
            if (TaskDialog.OSSupportsTaskDialogs)
            {
                using (TaskDialog dialog = new TaskDialog())
                {
                    dialog.Width = 200;
                    dialog.CenterParent = true;
                    dialog.WindowTitle = "About this program";
                    dialog.MainInstruction = "Inhouse Camguard 1.0";
                    dialog.Content = "A simple .NET application based on AForge libraries to analyze motion, capture image and data logging from a live webcam feed.";
                    dialog.ExpandedInformation = "Open source libraries used in this application: AForge.Core, AForge.Imaging, AForge.Math, AForge.Video, AForge.Vision, OxyPlot.Core, OxyPlot.Wpf, Ookii.Dialogs.Wpf.";
                    dialog.Footer = "Developed by Heiswayi Nrird - <a href=\"https://heiswayi.nrird.com\">https://heiswayi.nrird.com</a>";
                    dialog.FooterIcon = TaskDialogIcon.Information;
                    dialog.EnableHyperlinks = true;
                    TaskDialogButton okButton = new TaskDialogButton(ButtonType.Close);
                    //TaskDialogButton cancelButton = new TaskDialogButton(ButtonType.Cancel);
                    dialog.Buttons.Add(okButton);
                    //dialog.Buttons.Add(cancelButton);
                    dialog.HyperlinkClicked += new EventHandler<HyperlinkClickedEventArgs>(TaskDialogAbout_HyperLinkClicked);
                    TaskDialogButton button = dialog.ShowDialog(this);
                }
            }
            else
            {
                MessageBox.Show(this, "This operating system does not support task dialogs.", "About this program");
            }
        }

        private void TaskDialogAbout_HyperLinkClicked(object sender, HyperlinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://heiswayi.nrird.com");
        }
        #endregion

        #region Histogram window
        public event EventHandler<BitmapImage> OnBitmapUpdated;
        private void histogramMenuItem_Click(object sender, RoutedEventArgs e)
        {
            histogramWindow = new Histogram(this);
            histogramWindow.Show();
        }
        #endregion

        #region Trigger settings
        private void tbTriggerThreshold_GotFocus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(tbTriggerThreshold.Text))
            {
                tbTriggerThreshold.CaretIndex = tbTriggerThreshold.Text.Length;
            }
        }
        private void PlayBeep()
        {
            SoundPlayer audio = new SoundPlayer(InhouseCamguard.Properties.Resources.Beep);
            audio.Play();
        }
        private void cbEnableBeep_Checked(object sender, RoutedEventArgs e)
        {
            enableBeepOnTrigger = true;
        }
        private void cbEnableBeep_Unchecked(object sender, RoutedEventArgs e)
        {
            enableBeepOnTrigger = false;
        }
        private void btnBrowseSIL_Click(object sender, RoutedEventArgs e)
        {
            VistaFolderBrowserDialog dlg = new VistaFolderBrowserDialog();
            dlg.SelectedPath = tbSaveImgLocation.Text;
            dlg.ShowNewFolderButton = true;

            if (dlg.ShowDialog() == true)
            {
                tbSaveImgLocation.Text = dlg.SelectedPath;
            }
        }
        private void CaptureAsImage()
        {
            if (string.IsNullOrEmpty(tbSaveImgLocation.Text)) return;
            string fn = DateTime.Now.ToString("yyyyMMdd_hhmmss_fff") + ".png";
            string path = Path.Combine(tbSaveImgLocation.Text, fn);
            bitmapImageCopy.Save(path);
        }
        #endregion
    }
}