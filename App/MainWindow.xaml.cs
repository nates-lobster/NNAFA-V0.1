using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NeurofeedbackApp
{
    public class LoopingSampleProvider : ISampleProvider
    {
        private readonly AudioFileReader _reader;
        public LoopingSampleProvider(AudioFileReader reader) { _reader = reader; }
        public WaveFormat WaveFormat => _reader.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int read = _reader.Read(buffer, offset, count);
            if (read < count)
            {
                _reader.Position = 0;
                read += _reader.Read(buffer, offset + read, count - read);
            }
            return read;
        }
    }

    public partial class MainWindow : Window
    {
        private ClientWebSocket _webSocket = new ClientWebSocket();
        private CancellationTokenSource _cts = new CancellationTokenSource();

        // ScottPlot 5 Streamers for 4 Channels
        private ScottPlot.Plottables.DataStreamer _rawStreamTP9;
        private ScottPlot.Plottables.DataStreamer _filtStreamTP9;
        private ScottPlot.Plottables.DataStreamer _rawStreamAF7;
        private ScottPlot.Plottables.DataStreamer _filtStreamAF7;
        private ScottPlot.Plottables.DataStreamer _rawStreamAF8;
        private ScottPlot.Plottables.DataStreamer _filtStreamAF8;
        private ScottPlot.Plottables.DataStreamer _rawStreamTP10;
        private ScottPlot.Plottables.DataStreamer _filtStreamTP10;
        
        private ScottPlot.Plottables.DataStreamer _deltaTrend;
        private ScottPlot.Plottables.DataStreamer _thetaTrend;
        private ScottPlot.Plottables.DataStreamer _alphaTrend;
        private ScottPlot.Plottables.DataStreamer _betaTrend;
        private ScottPlot.Plottables.DataStreamer _gammaTrend;
        
        private ScottPlot.Plottables.Scatter _fftScatter;
        private ScottPlot.Plottables.Scatter _nfFftScatter; // Duplicate for NF tab

        private double _currentDelta = 0;
        private double _currentTheta = 0;
        private double _currentAlpha = 0;
        private double _currentBeta = 0;
        private double _currentGamma = 0;

        // NAudio Properties
        private WaveOutEvent _waveOut;
        private SignalGenerator _signalGenerator;
        private VolumeSampleProvider _volumeProvider;
        private double _targetRatio = 1.0;
        private float _targetVolume = 0.0f;
        private float _masterVolume = 0.5f;
        private System.Windows.Threading.DispatcherTimer _volumeLerpTimer;

        // Calibration Properties
        private bool _isCalibrating = false;
        private DateTime _calibrationStartTime;
        private System.Windows.Threading.DispatcherTimer _calibrationTimer;
        private System.Collections.Generic.List<double> _calibrationRatios = new System.Collections.Generic.List<double>();

        public MainWindow()
        {
            InitializeComponent();
            SetupPlots();
            SetupAudio();
            _ = ConnectToEngineAsync();
        }

        private void SetupAudio()
        {
            _waveOut = new WaveOutEvent();
            // Generate a calming 432Hz sine wave
            _signalGenerator = new SignalGenerator(44100, 1)
            {
                Type = SignalGeneratorType.Sin,
                Frequency = 432.0,
                Gain = 0.2 // Base low gain
            };

            _volumeProvider = new VolumeSampleProvider(_signalGenerator);
            _volumeProvider.Volume = 0.0f; // Start muted

            _waveOut.Init(_volumeProvider);
            // We do not Play() until checkbox is ticked

            _volumeLerpTimer = new System.Windows.Threading.DispatcherTimer();
            _volumeLerpTimer.Interval = TimeSpan.FromMilliseconds(50);
            _volumeLerpTimer.Tick += VolumeLerpTimer_Tick;
            _volumeLerpTimer.Start();
        }

        private void VolumeLerpTimer_Tick(object? sender, EventArgs e)
        {
            if (_volumeProvider != null)
            {
                // Smoothly lerp the actual volume towards the target
                float diff = _targetVolume - _volumeProvider.Volume;
                _volumeProvider.Volume += diff * 0.1f; // 10% closer every 50ms
                VolumeProgressBar.Value = _volumeProvider.Volume / (_masterVolume > 0 ? _masterVolume : 1);
            }
        }

        private void BrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Audio Files|*.mp3;*.wav|All Files|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    bool wasPlaying = _waveOut.PlaybackState == PlaybackState.Playing;
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = new WaveOutEvent();

                    var reader = new AudioFileReader(openFileDialog.FileName);
                    var loopingProvider = new LoopingSampleProvider(reader);
                    
                    _volumeProvider = new VolumeSampleProvider(loopingProvider);
                    _volumeProvider.Volume = _targetVolume;
                    _waveOut.Init(_volumeProvider);
                    
                    if (wasPlaying) _waveOut.Play();
                    
                    if (LoadedAudioText != null)
                        LoadedAudioText.Text = System.IO.Path.GetFileName(openFileDialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading audio: " + ex.Message);
                }
            }
        }

        private void MasterVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _masterVolume = (float)e.NewValue;
        }

        private void SetupPlots()
        {
            SetupEegPlot(EegPlotTP9, "TP9 (Left Ear)", out _rawStreamTP9, out _filtStreamTP9);
            SetupEegPlot(EegPlotTP10, "TP10 (Right Ear)", out _rawStreamTP10, out _filtStreamTP10);
            SetupEegPlot(EegPlotAF7, "AF7 (Left Forehead)", out _rawStreamAF7, out _filtStreamAF7);
            SetupEegPlot(EegPlotAF8, "AF8 (Right Forehead)", out _rawStreamAF8, out _filtStreamAF8);

            SetupFftPlot(FftPlot, out _fftScatter);
            SetupFftPlot(NfFftPlot, out _nfFftScatter);

            TrendPlot.Plot.Title("Brainwave Power Trends Over Time (Shift+Scroll to Zoom Y)");
            TrendPlot.Plot.XLabel("Time (Scrolling)");
            TrendPlot.Plot.YLabel("Power");
            
            int trendPoints = 1000;
            _deltaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints);
            _deltaTrend.Color = ScottPlot.Color.FromHex("#FF000088"); _deltaTrend.LineWidth = 2; _deltaTrend.ViewScrollLeft();
            
            _thetaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints);
            _thetaTrend.Color = ScottPlot.Color.FromHex("#FF008888"); _thetaTrend.LineWidth = 2; _thetaTrend.ViewScrollLeft();
            
            _alphaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints);
            _alphaTrend.Color = ScottPlot.Color.FromHex("#FF008800"); _alphaTrend.LineWidth = 2; _alphaTrend.ViewScrollLeft();
            
            _betaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints);
            _betaTrend.Color = ScottPlot.Color.FromHex("#FF880000"); _betaTrend.LineWidth = 2; _betaTrend.ViewScrollLeft();
            
            _gammaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints);
            _gammaTrend.Color = ScottPlot.Color.FromHex("#FF880088"); _gammaTrend.LineWidth = 2; _gammaTrend.ViewScrollLeft();
            
            TrendPlot.Plot.Axes.SetLimitsY(0, 50);
            
            EegPlotTP9.Refresh(); EegPlotTP10.Refresh(); EegPlotAF7.Refresh(); EegPlotAF8.Refresh();
            FftPlot.Refresh(); NfFftPlot.Refresh(); TrendPlot.Refresh();
        }

        private void SetupEegPlot(ScottPlot.WPF.WpfPlot plot, string title, out ScottPlot.Plottables.DataStreamer raw, out ScottPlot.Plottables.DataStreamer filt)
        {
            plot.Plot.Title(title);
            plot.Plot.Axes.SetLimitsY(-150, 150);
            
            raw = plot.Plot.Add.DataStreamer(250);
            raw.Color = ScottPlot.Colors.LightGray; raw.LineWidth = 1; raw.ViewScrollLeft();
            
            filt = plot.Plot.Add.DataStreamer(250);
            filt.Color = ScottPlot.Colors.Blue; filt.LineWidth = 2; filt.ViewScrollLeft();
        }

        private void SetupFftPlot(ScottPlot.WPF.WpfPlot plot, out ScottPlot.Plottables.Scatter scatter)
        {
            plot.Plot.Title("AF7 FFT Power Spectral Density (Shift+Scroll to Zoom Y)");
            plot.Plot.XLabel("Frequency (Hz)");
            plot.Plot.YLabel("Power");
            
            double[] empty = { 0, 1 };
            scatter = plot.Plot.Add.ScatterLine(empty, empty);
            scatter.Color = ScottPlot.Colors.Purple;
            scatter.LineWidth = 2;

            // Delta (1-4)
            var deltaBox = plot.Plot.Add.Rectangle(1, 4, 0, 99999);
            deltaBox.FillColor = new ScottPlot.Color(0, 0, 255, 30); deltaBox.LineColor = ScottPlot.Colors.Transparent;
            // Theta (4-8)
            var thetaBox = plot.Plot.Add.Rectangle(4, 8, 0, 99999);
            thetaBox.FillColor = new ScottPlot.Color(0, 255, 255, 50); thetaBox.LineColor = ScottPlot.Colors.Transparent;
            // Alpha (8-12)
            var alphaBox = plot.Plot.Add.Rectangle(8, 12, 0, 99999);
            alphaBox.FillColor = new ScottPlot.Color(0, 255, 0, 50); alphaBox.LineColor = ScottPlot.Colors.Transparent;
            // Beta (12-30)
            var betaBox = plot.Plot.Add.Rectangle(12, 30, 0, 99999);
            betaBox.FillColor = new ScottPlot.Color(255, 0, 0, 50); betaBox.LineColor = ScottPlot.Colors.Transparent;
            // Gamma (30-40)
            var gammaBox = plot.Plot.Add.Rectangle(30, 40, 0, 99999);
            gammaBox.FillColor = new ScottPlot.Color(255, 0, 255, 30); gammaBox.LineColor = ScottPlot.Colors.Transparent;
            
            plot.Plot.Axes.SetLimits(0, 40, 0, 20);
        }

        private async Task ConnectToEngineAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    Dispatcher.Invoke(() => StatusText.Text = "Connecting...");
                    _webSocket = new ClientWebSocket();
                    await _webSocket.ConnectAsync(new Uri("ws://localhost:8765"), _cts.Token);
                    Dispatcher.Invoke(() => StatusText.Text = "Connected");

                    await SendConfigToEngine();
                    await ReceiveDataLoopAsync();
                }
                catch (Exception)
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "Connection lost. Retrying...";
                        StatusIndicator.Background = Brushes.Gray;
                    });
                    await Task.Delay(2000); // Retry every 2 seconds
                }
            }
        }

        private async Task SendConfigToEngine()
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                double threshold = 150;
                double lowCut = 1.0;
                double highCut = 40.0;
                Dispatcher.Invoke(() => { 
                    threshold = ThresholdSlider.Value; 
                    lowCut = LowCutSlider != null ? LowCutSlider.Value : 1.0;
                    highCut = HighCutSlider != null ? HighCutSlider.Value : 40.0;
                });
                
                var config = new { type = "config", threshold = threshold, low_cut = lowCut, high_cut = highCut };
                string json = JsonSerializer.Serialize(config);
                await _webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, _cts.Token);
            }
        }

        private async Task ReceiveDataLoopAsync()
        {
            var buffer = new byte[65536];
            while (_webSocket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try
                {
                    using var doc = JsonDocument.Parse(message);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "telemetry")
                    {
                        UpdateUI(root);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"JSON Parse error: {ex.Message}"); }
            }
        }

        private void UpdateUI(JsonElement data)
        {
            Dispatcher.Invoke(() =>
            {
                string status = data.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "UNKNOWN" : "UNKNOWN";
                StatusText.Text = status;
                
                if (status == "OK") StatusIndicator.Background = Brushes.LimeGreen;
                else if (status == "HOLD") StatusIndicator.Background = Brushes.Orange;
                else StatusIndicator.Background = Brushes.Red;

                if (data.TryGetProperty("delta", out var deltaProp)) _currentDelta = deltaProp.GetDouble();
                if (data.TryGetProperty("theta", out var thetaProp)) _currentTheta = thetaProp.GetDouble();
                if (data.TryGetProperty("alpha", out var alphaProp)) _currentAlpha = alphaProp.GetDouble();
                if (data.TryGetProperty("beta", out var betaProp)) _currentBeta = betaProp.GetDouble();
                if (data.TryGetProperty("gamma", out var gammaProp)) _currentGamma = gammaProp.GetDouble();

                DeltaText.Text = _currentDelta.ToString("F2");
                ThetaText.Text = _currentTheta.ToString("F2");
                AlphaText.Text = _currentAlpha.ToString("F2");
                BetaText.Text = _currentBeta.ToString("F2");
                GammaText.Text = _currentGamma.ToString("F2");

                CalculateRatio();

                ProcessEegData(data, "tp9", _rawStreamTP9, _filtStreamTP9, EegPlotTP9);
                ProcessEegData(data, "af7", _rawStreamAF7, _filtStreamAF7, EegPlotAF7);
                ProcessEegData(data, "af8", _rawStreamAF8, _filtStreamAF8, EegPlotAF8);
                ProcessEegData(data, "tp10", _rawStreamTP10, _filtStreamTP10, EegPlotTP10);

                if (status == "OK" || status == "HOLD")
                {
                    _deltaTrend.Add(_currentDelta);
                    _thetaTrend.Add(_currentTheta);
                    _alphaTrend.Add(_currentAlpha);
                    _betaTrend.Add(_currentBeta);
                    _gammaTrend.Add(_currentGamma);
                    TrendPlot.Refresh();
                }

                if (data.TryGetProperty("fft_freqs", out var freqsProp) && data.TryGetProperty("fft_psd", out var psdProp))
                {
                    var freqsList = freqsProp.EnumerateArray();
                    var psdList = psdProp.EnumerateArray();
                    
                    int count = freqsProp.GetArrayLength();
                    double[] freqsArray = new double[count];
                    double[] psdArray = new double[count];
                    
                    int j = 0; foreach (var val in freqsList) freqsArray[j++] = val.GetDouble();
                    j = 0; foreach (var val in psdList) psdArray[j++] = val.GetDouble();

                    FftPlot.Plot.Remove(_fftScatter);
                    _fftScatter = FftPlot.Plot.Add.ScatterLine(freqsArray, psdArray);
                    _fftScatter.Color = ScottPlot.Colors.Purple;
                    _fftScatter.LineWidth = 2;
                    FftPlot.Refresh();

                    NfFftPlot.Plot.Remove(_nfFftScatter);
                    _nfFftScatter = NfFftPlot.Plot.Add.ScatterLine(freqsArray, psdArray);
                    _nfFftScatter.Color = ScottPlot.Colors.Purple;
                    _nfFftScatter.LineWidth = 2;
                    NfFftPlot.Refresh();
                }
            });
        }

        private void ProcessEegData(JsonElement data, string channel, ScottPlot.Plottables.DataStreamer rawStream, ScottPlot.Plottables.DataStreamer filtStream, ScottPlot.WPF.WpfPlot plot)
        {
            if (data.TryGetProperty($"new_raw_{channel}", out var rawList) && data.TryGetProperty($"new_filt_{channel}", out var filtList))
            {
                int rawCount = rawList.GetArrayLength();
                double[] rawArr = new double[rawCount];
                double rawSum = 0; int i = 0;
                foreach (var val in rawList.EnumerateArray()) { rawArr[i] = val.GetDouble(); rawSum += rawArr[i]; i++; }
                double rawMean = i > 0 ? rawSum / i : 0;

                foreach (var val in rawArr) rawStream.Add(val - rawMean);
                foreach (var val in filtList.EnumerateArray()) filtStream.Add(val.GetDouble());
                
                plot.Refresh();
            }
        }

        private void CalculateRatio()
        {
            if (RatioNumCombo == null || RatioDenCombo == null || RatioText == null) return;
            
            string numStr = ((ComboBoxItem)RatioNumCombo.SelectedItem)?.Content?.ToString() ?? "Alpha";
            string denStr = ((ComboBoxItem)RatioDenCombo.SelectedItem)?.Content?.ToString() ?? "Beta";

            double num = GetBandValue(numStr);
            double den = GetBandValue(denStr);

            double ratio = den > 0 ? num / den : 0;
            RatioText.Text = ratio.ToString("F2");
            FftRatioText.Text = ratio.ToString("F2");

            UpdateNeurofeedbackVolume(ratio);
        }

        private void UpdateNeurofeedbackVolume(double currentRatio)
        {
            // Normalize ratio against target (0.0 to 1.0)
            // If currentRatio >= _targetRatio, volume is 0 (silence is success)
            // If currentRatio = 0, volume is 1.0 (loudest)
            double invertedScore = 1.0 - (currentRatio / _targetRatio);
            if (invertedScore < 0) invertedScore = 0;
            if (invertedScore > 1) invertedScore = 1;

            _targetVolume = (float)invertedScore * _masterVolume;
        }

        private double GetBandValue(string bandName)
        {
            return bandName switch {
                "Delta" => _currentDelta, "Theta" => _currentTheta, "Alpha" => _currentAlpha, "Beta" => _currentBeta, "Gamma" => _currentGamma, _ => 0
            };
        }

        private void RatioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { CalculateRatio(); }

        private async void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ThresholdValueText != null) ThresholdValueText.Text = $"{Math.Round(e.NewValue)} uV";
            await SendConfigToEngine();
        }

        private void TargetRatioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TargetRatioValueText != null)
            {
                _targetRatio = e.NewValue;
                TargetRatioValueText.Text = _targetRatio.ToString("F1");
            }
        }

        private async void FilterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LowCutValueText != null && LowCutSlider != null)
                LowCutValueText.Text = $"{Math.Round(LowCutSlider.Value, 1)} Hz";
            if (HighCutValueText != null && HighCutSlider != null)
                HighCutValueText.Text = $"{Math.Round(HighCutSlider.Value, 1)} Hz";
            
            await SendConfigToEngine();
        }

        private void CalibrateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCalibrating) return;

            _isCalibrating = true;
            _calibrationRatios.Clear();
            _calibrationStartTime = DateTime.Now;

            CalibrateButton.IsEnabled = false;
            CalibrationStatusText.Text = "Calibrating (0/60s)...";
            CalibrationProgressBar.Value = 0;

            _calibrationTimer = new System.Windows.Threading.DispatcherTimer();
            _calibrationTimer.Interval = TimeSpan.FromMilliseconds(500);
            _calibrationTimer.Tick += CalibrationTimer_Tick;
            _calibrationTimer.Start();
        }

        private void CalibrationTimer_Tick(object? sender, EventArgs e)
        {
            var elapsed = (DateTime.Now - _calibrationStartTime).TotalSeconds;
            CalibrationProgressBar.Value = elapsed;
            CalibrationStatusText.Text = $"Calibrating ({Math.Round(elapsed)}/60s)...";

            // Only collect data between 10s and 50s (drop first/last 10s of a 60s window)
            if (elapsed >= 10 && elapsed <= 50)
            {
                if (double.TryParse(RatioText.Text, out double currentRatio))
                {
                    _calibrationRatios.Add(currentRatio);
                }
            }

            if (elapsed >= 60)
            {
                _calibrationTimer.Stop();
                _isCalibrating = false;
                CalibrateButton.IsEnabled = true;

                if (_calibrationRatios.Count > 0)
                {
                    double sum = 0;
                    foreach (var val in _calibrationRatios) sum += val;
                    double averageRatio = sum / _calibrationRatios.Count;

                    // Update the target ratio slider automatically
                    TargetRatioSlider.Value = averageRatio;
                    CalibrationStatusText.Text = $"Done. Baseline set to {averageRatio:F2}";
                }
                else
                {
                    CalibrationStatusText.Text = "Failed. No valid data.";
                }
            }
        }

        private void AudioEnable_Checked(object sender, RoutedEventArgs e) { _waveOut.Play(); }
        private void AudioEnable_Unchecked(object sender, RoutedEventArgs e) { _waveOut.Pause(); }

        private Process? _engineProcess;

        private void AppendToConsole(string text)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ConsoleOutput.AppendText(text + Environment.NewLine);
                ConsoleOutput.ScrollToEnd();
            });
        }

        private void LaunchEngine_Click(object sender, RoutedEventArgs e)
        {
            if (_engineProcess != null && !_engineProcess.HasExited)
            {
                AppendToConsole("Engine is already running.");
                return;
            }

            try
            {
                string enginePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\PyApp\engine.py");
                enginePath = System.IO.Path.GetFullPath(enginePath);
                
                if (System.IO.File.Exists(enginePath))
                {
                    _engineProcess = new Process();
                    _engineProcess.StartInfo.FileName = "python";
                    // Use -u to force unbuffered output so we get live updates in the console
                    _engineProcess.StartInfo.Arguments = $"-u \"{enginePath}\"";
                    _engineProcess.StartInfo.UseShellExecute = false;
                    _engineProcess.StartInfo.CreateNoWindow = true;
                    _engineProcess.StartInfo.RedirectStandardOutput = true;
                    _engineProcess.StartInfo.RedirectStandardError = true;
                    _engineProcess.StartInfo.WorkingDirectory = System.IO.Path.GetDirectoryName(enginePath) ?? "";

                    _engineProcess.OutputDataReceived += (s, args) => { if (args.Data != null) AppendToConsole("[Engine] " + args.Data); };
                    _engineProcess.ErrorDataReceived += (s, args) => { if (args.Data != null) AppendToConsole("[Engine ERROR] " + args.Data); };

                    _engineProcess.Start();
                    _engineProcess.BeginOutputReadLine();
                    _engineProcess.BeginErrorReadLine();
                    
                    AppendToConsole("Started Python Engine (Hidden).");
                }
                else
                {
                    AppendToConsole("engine.py not found at: " + enginePath);
                }
            }
            catch (Exception ex)
            {
                AppendToConsole("Could not launch Python Engine. Error: " + ex.Message);
            }
        }

        private void LaunchBlueMuse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendToConsole("Starting BlueMuse via protocol handler...");
                // BlueMuse CLI standard protocol to launch and immediately start streaming the first device
                Process.Start(new ProcessStartInfo
                {
                    FileName = "bluemuse://start?streamfirst=true",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendToConsole("Could not launch BlueMuse. Ensure it is installed. Error: " + ex.Message);
            }
        }

        private void LaunchLabRecorder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string labRecorderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\Software\LabRecorder-1.17.0-Win_amd64\LabRecorder.exe");
                labRecorderPath = System.IO.Path.GetFullPath(labRecorderPath);

                if (System.IO.File.Exists(labRecorderPath))
                {
                    AppendToConsole("Starting LabRecorder GUI...");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = labRecorderPath,
                        WorkingDirectory = System.IO.Path.GetDirectoryName(labRecorderPath)
                    });
                }
                else
                {
                    AppendToConsole("LabRecorder executable not found at: " + labRecorderPath);
                }
            }
            catch (Exception ex)
            {
                AppendToConsole("Error launching LabRecorder: " + ex.Message);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _cts.Cancel();
            _waveOut?.Stop();
            _waveOut?.Dispose();

            if (_engineProcess != null && !_engineProcess.HasExited)
            {
                try
                {
                    _engineProcess.Kill();
                }
                catch { }
            }
        }
    }
}
