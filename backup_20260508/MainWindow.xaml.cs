using System;
using System.IO;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Collections.ObjectModel;
using System.Linq;
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
        private ClientWebSocket _emuSocket = new ClientWebSocket();
        private readonly SemaphoreSlim _wsLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _emuLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private ScottPlot.Plottables.DataStreamer? _rawStreamTP9, _filtStreamTP9, _rawStreamAF7, _filtStreamAF7, _rawStreamAF8, _filtStreamAF8, _rawStreamTP10, _filtStreamTP10;
        private ScottPlot.Plottables.DataStreamer? _pipeRaw, _pipeFilt, _pipeDen;
        private ScottPlot.Plottables.DataStreamer? _deltaTrend, _thetaTrend, _alphaTrend, _betaTrend, _gammaTrend;
        private ScottPlot.Plottables.Scatter? _fftScatter, _nfFftScatter, _pipeFft;
        private ScottPlot.Plottables.DataStreamer? _emuDelta, _emuTheta, _emuAlpha, _emuBeta, _emuGamma;
        private ScottPlot.Plottables.DataStreamer? _softDelta, _softTheta, _softAlpha, _softBeta, _softGamma;

        public class OscillatorInfo
        {
            public double Hz { get; set; }
            public double Amp { get; set; }
            public string DisplayName => $"{Hz} Hz @ {Amp} uV";
        }

        private ObservableCollection<OscillatorInfo> _activeOscillators = new ObservableCollection<OscillatorInfo>();
        private readonly object _oscLock = new object();
        private double _currentDelta = 0, _currentTheta = 0, _currentAlpha = 0, _currentBeta = 0, _currentGamma = 0;

        private WaveOutEvent? _waveOut;
        private SignalGenerator? _signalGenerator;
        private VolumeSampleProvider? _volumeProvider;
        private double _targetRatio = 1.0;
        private float _targetVolume = 0.0f;
        private float _masterVolume = 0.5f;
        private System.Windows.Threading.DispatcherTimer? _volumeLerpTimer;

        private bool _isCalibrating = false;
        private DateTime _calibrationStartTime;
        private System.Windows.Threading.DispatcherTimer? _calibrationTimer;
        private System.Collections.Generic.List<double> _calibrationRatios = new System.Collections.Generic.List<double>();

        public MainWindow()
        {
            try {
                InitializeComponent();
                BindingOperations.EnableCollectionSynchronization(_activeOscillators, _oscLock);
                ActiveFreqsList.ItemsSource = _activeOscillators;
                SetupPlots();
                SetupAudio();
                _ = ConnectToEngineAsync();
                // ConnectToEmulatorAsync is not called here to ensure emulator is dead upon startup
            } catch (Exception ex) {
                MessageBox.Show("Fatal Error during initialization: " + ex.Message + "\n\n" + ex.StackTrace);
            }
        }

        private void SetupAudio()
        {
            try {
                _waveOut = new WaveOutEvent();
                _signalGenerator = new SignalGenerator(44100, 1) { Type = SignalGeneratorType.Sin, Frequency = 432.0, Gain = 0.2 };
                _volumeProvider = new VolumeSampleProvider(_signalGenerator) { Volume = 0.0f };
                _waveOut.Init(_volumeProvider);
                _volumeLerpTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _volumeLerpTimer.Tick += (s, e) => {
                    if (_volumeProvider != null) {
                        float diff = _targetVolume - _volumeProvider.Volume;
                        _volumeProvider.Volume += diff * 0.1f;
                        VolumeProgressBar.Value = _volumeProvider.Volume / (_masterVolume > 0 ? _masterVolume : 1);
                    }
                };
                _volumeLerpTimer.Start();
            } catch (Exception ex) { AppendToConsole("Audio Error: " + ex.Message); }
        }

        private void SetupPlots()
        {
            try {
                SetupEegPlot(EegPlotTP9, "TP9 (Left Ear)", out _rawStreamTP9, out _filtStreamTP9);
                SetupEegPlot(EegPlotTP10, "TP10 (Right Ear)", out _rawStreamTP10, out _filtStreamTP10);
                SetupEegPlot(EegPlotAF7, "AF7 (Left Forehead)", out _rawStreamAF7, out _filtStreamAF7);
                SetupEegPlot(EegPlotAF8, "AF8 (Right Forehead)", out _rawStreamAF8, out _filtStreamAF8);
                SetupFftPlot(FftPlot, out _fftScatter);
                SetupFftPlot(NfFftPlot, out _nfFftScatter);

                SetupEegPlot(PipelinePlotRaw, "Stage 1: Raw Signal (uV)", out _pipeRaw, out _);
                _pipeRaw.Color = ScottPlot.Colors.Gray;
                SetupEegPlot(PipelinePlotFiltered, "Stage 2: Filtered (1-40Hz + Notch)", out _, out _pipeFilt);
                _pipeFilt.Color = ScottPlot.Colors.Blue;
                SetupEegPlot(PipelinePlotDenoised, "Stage 3: Denoised (Artifact Rejection)", out _, out _pipeDen);
                _pipeDen.Color = ScottPlot.Colors.Green;
                SetupFftPlot(PipelinePlotFft, out _pipeFft);
                PipelinePlotFft.Plot.Title("Stage 4: FFT (Power Spectrum)"); PipelinePlotRaw.Plot.XLabel("Samples"); PipelinePlotRaw.Plot.YLabel("Amplitude (uV)"); PipelinePlotFiltered.Plot.XLabel("Samples"); PipelinePlotFiltered.Plot.YLabel("Amplitude (uV)"); PipelinePlotDenoised.Plot.XLabel("Samples"); PipelinePlotDenoised.Plot.YLabel("Amplitude (uV)"); PipelinePlotFft.Plot.XLabel("Frequency (Hz)"); PipelinePlotFft.Plot.YLabel("Power (uV2/Hz)");

                // MATCHING STATUS BAR COLORS (#RRGGBB format to ensure 100% visibility)
                var cDelta = ScottPlot.Color.FromHex("#000088"); // Blue
                var cTheta = ScottPlot.Color.FromHex("#008888"); // Teal
                var cAlpha = ScottPlot.Color.FromHex("#008800"); // Green
                var cBeta = ScottPlot.Color.FromHex("#880000");  // Red
                var cGamma = ScottPlot.Color.FromHex("#880088"); // Magenta

                SetupEmuPlot(EmuPlotDelta, SoftPlotDelta, "Delta (1-4 Hz)", cDelta, out _emuDelta, out _softDelta);
                SetupEmuPlot(EmuPlotTheta, SoftPlotTheta, "Theta (4-8 Hz)", cTheta, out _emuTheta, out _softTheta);
                SetupEmuPlot(EmuPlotAlpha, SoftPlotAlpha, "Alpha (8-12 Hz)", cAlpha, out _emuAlpha, out _softAlpha);
                SetupEmuPlot(EmuPlotBeta, SoftPlotBeta, "Beta (12-30 Hz)", cBeta, out _emuBeta, out _softBeta);
                SetupEmuPlot(EmuPlotGamma, SoftPlotGamma, "Gamma (30-40 Hz)", cGamma, out _emuGamma, out _softGamma);

                // Initialize Debug Pipeline Plots
                PipelinePlotRaw.Plot.Title("Stage 1: Raw Signal (uV)");
                _pipeRaw = PipelinePlotRaw.Plot.Add.DataStreamer(1000);
                _pipeRaw.Color = ScottPlot.Colors.Gray;
                PipelinePlotRaw.Plot.Axes.SetLimitsY(-200, 200);
                PipelinePlotRaw.Plot.XLabel("Samples");
                PipelinePlotRaw.Plot.YLabel("Amplitude (uV)");

                PipelinePlotFiltered.Plot.Title("Stage 2: Filtered (1-40Hz + Notch)");
                _pipeFilt = PipelinePlotFiltered.Plot.Add.DataStreamer(1000);
                _pipeFilt.Color = ScottPlot.Colors.Blue;
                PipelinePlotFiltered.Plot.Axes.SetLimitsY(-100, 100);
                PipelinePlotFiltered.Plot.XLabel("Samples");
                PipelinePlotFiltered.Plot.YLabel("Amplitude (uV)");

                PipelinePlotDenoised.Plot.Title("Stage 3: Denoised (Artifact Rejection)");
                _pipeDen = PipelinePlotDenoised.Plot.Add.DataStreamer(1000);
                _pipeDen.Color = ScottPlot.Colors.Green;
                PipelinePlotDenoised.Plot.Axes.SetLimitsY(-100, 100);
                PipelinePlotDenoised.Plot.XLabel("Samples");
                PipelinePlotDenoised.Plot.YLabel("Amplitude (uV)");

                PipelinePlotFft.Plot.Title("Stage 4: FFT (Power Spectrum)");
                PipelinePlotFft.Plot.Axes.SetLimits(0, 40, 0, 20);
                PipelinePlotFft.Plot.XLabel("Frequency (Hz)");
                PipelinePlotFft.Plot.YLabel("Power (uV2/Hz)");

                TrendPlot.Plot.Title("Brainwave Power Trends Over Time");
                int trendPoints = 1000;
                _deltaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints); _deltaTrend.Color = cDelta; _deltaTrend.ViewScrollLeft(); _deltaTrend.LineWidth = 3;
                _thetaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints); _thetaTrend.Color = cTheta; _thetaTrend.ViewScrollLeft(); _thetaTrend.LineWidth = 3;
                _alphaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints); _alphaTrend.Color = cAlpha; _alphaTrend.ViewScrollLeft(); _alphaTrend.LineWidth = 3;
                _betaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints); _betaTrend.Color = cBeta; _betaTrend.ViewScrollLeft(); _betaTrend.LineWidth = 3;
                _gammaTrend = TrendPlot.Plot.Add.DataStreamer(trendPoints); _gammaTrend.Color = cGamma; _gammaTrend.ViewScrollLeft(); _gammaTrend.LineWidth = 3;
                TrendPlot.Plot.Axes.SetLimitsY(0, 50);
                
                EegPlotTP9.Refresh(); EegPlotTP10.Refresh(); EegPlotAF7.Refresh(); EegPlotAF8.Refresh(); FftPlot.Refresh(); NfFftPlot.Refresh(); TrendPlot.Refresh();
            } catch (Exception ex) { MessageBox.Show("Plot Setup Error: " + ex.Message); }
        }

        private void TrendVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (_deltaTrend != null) _deltaTrend.IsVisible = ShowDeltaTrend.IsChecked == true;
            if (_thetaTrend != null) _thetaTrend.IsVisible = ShowThetaTrend.IsChecked == true;
            if (_alphaTrend != null) _alphaTrend.IsVisible = ShowAlphaTrend.IsChecked == true;
            if (_betaTrend != null)  _betaTrend.IsVisible  = ShowBetaTrend.IsChecked == true;
            if (_gammaTrend != null) _gammaTrend.IsVisible = ShowGammaTrend.IsChecked == true;
            TrendPlot.Refresh();
        }

        private void SetupEmuPlot(ScottPlot.WPF.WpfPlot emu, ScottPlot.WPF.WpfPlot soft, string title, ScottPlot.Color color, out ScottPlot.Plottables.DataStreamer emuStream, out ScottPlot.Plottables.DataStreamer softStream)
        {
            emu.Plot.Title(title + " [Emulator]"); emu.Plot.Axes.SetLimitsY(-100, 100);
            emuStream = emu.Plot.Add.DataStreamer(250); emuStream.Color = color; emuStream.LineWidth = 2.5f; emuStream.ViewScrollLeft();
            soft.Plot.Title(title + " [Software]"); soft.Plot.Axes.SetLimitsY(-100, 100);
            softStream = soft.Plot.Add.DataStreamer(250); softStream.Color = ScottPlot.Colors.Red; softStream.LineWidth = 1.5f; softStream.LinePattern = ScottPlot.LinePattern.Dotted; softStream.ViewScrollLeft();
            emu.Refresh(); soft.Refresh();
        }

        private void SetupEegPlot(ScottPlot.WPF.WpfPlot plot, string title, out ScottPlot.Plottables.DataStreamer raw, out ScottPlot.Plottables.DataStreamer filt)
        {
            plot.Plot.Title(title); plot.Plot.Axes.SetLimitsY(-150, 150);
            raw = plot.Plot.Add.DataStreamer(250); raw.Color = ScottPlot.Colors.LightGray; raw.ViewScrollLeft(); raw.ManageAxisLimits = false; raw.LineWidth = 1.5f;
            filt = plot.Plot.Add.DataStreamer(250); filt.Color = ScottPlot.Colors.Blue; filt.ViewScrollLeft(); filt.ManageAxisLimits = false; filt.LineWidth = 2;
        }

        private void SetupFftPlot(ScottPlot.WPF.WpfPlot plot, out ScottPlot.Plottables.Scatter scatter)
        {
            plot.Plot.Title("PSD");

            // ScottPlot 5 FromHex is RRGGBBAA. 20% alpha is approx hex "33" at the end.
            var cDelta = ScottPlot.Color.FromHex("#00008833");
            var cTheta = ScottPlot.Color.FromHex("#00888833");
            var cAlpha = ScottPlot.Color.FromHex("#00880033");
            var cBeta = ScottPlot.Color.FromHex("#88000033");
            var cGamma = ScottPlot.Color.FromHex("#88008833");

            plot.Plot.Add.HorizontalSpan(1, 4).FillStyle.Color = cDelta;
            plot.Plot.Add.HorizontalSpan(4, 8).FillStyle.Color = cTheta;
            plot.Plot.Add.HorizontalSpan(8, 12).FillStyle.Color = cAlpha;
            plot.Plot.Add.HorizontalSpan(12, 30).FillStyle.Color = cBeta;
            plot.Plot.Add.HorizontalSpan(30, 40).FillStyle.Color = cGamma;

            scatter = plot.Plot.Add.ScatterLine(new double[] { 0, 1 }, new double[] { 0, 1 });
            scatter.Color = ScottPlot.Colors.Purple; plot.Plot.Axes.SetLimits(0, 40, 0, 20);
        }


        private async Task ConnectToEngineAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    Dispatcher.Invoke(() => StatusText.Text = "Connecting...");
                    _webSocket = new ClientWebSocket();
                    await _webSocket.ConnectAsync(new Uri("ws://127.0.0.1:8765"), _cts.Token);
                    Dispatcher.Invoke(() => StatusText.Text = "Connected");
                    await SendConfigToEngine();
                    await ReceiveDataLoopAsync();
                }
                catch (Exception) { Dispatcher.Invoke(() => { StatusText.Text = "Searching..."; StatusIndicator.Background = Brushes.Gray; }); await Task.Delay(5000); }
            }
        }

        private async Task ConnectToEmulatorAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    _emuSocket = new ClientWebSocket();
                    await _emuSocket.ConnectAsync(new Uri("ws://127.0.0.1:8766"), _cts.Token);
                    AppendToConsole("Emulator Connected.");
                    double noise = 0; Dispatcher.Invoke(() => noise = EmuNoiseSlider.Value);
                    OscillatorInfo[] snapshot;
                    lock(_oscLock) { snapshot = _activeOscillators.ToArray(); }
                    foreach (var osc in snapshot) await SendEmuCommand("add_freq", osc.Hz, osc.Amp);
                    await SendEmuCommand("set_noise", 0, noise);
                    while (_emuSocket.State == WebSocketState.Open) await Task.Delay(1000);
                }
                catch (Exception) { await Task.Delay(3000); }
            }
        }

        private async Task SendEmuCommand(string cmd, double hz = 0, double val = 0)
        {
            try {
                await _emuLock.WaitAsync(_cts.Token);
                if (_emuSocket != null && _emuSocket.State == WebSocketState.Open) {
                    var payload = new { command = cmd, hz = hz, amp = val, value = val };
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                    await _emuSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
                }
            } catch (Exception ex) { Debug.WriteLine("Emu Send Error: " + ex.Message); }
            finally { _emuLock.Release(); }
        }

        private async void AddFreq_Click(object sender, RoutedEventArgs e) { 
            if (double.TryParse(EmuHzInput.Text, out double hz) && double.TryParse(EmuAmpInput.Text, out double amp)) { 
                var osc = new OscillatorInfo { Hz = hz, Amp = amp }; 
                lock(_oscLock) { _activeOscillators.Add(osc); }
                await SendEmuCommand("add_freq", hz, amp); 
            } 
        }
        private async void RemoveFreq_Click(object sender, RoutedEventArgs e) { 
            if (sender is Button btn && btn.Tag is double hz) { 
                lock(_oscLock) {
                    for (int i = 0; i < _activeOscillators.Count; i++) { 
                        if (_activeOscillators[i].Hz == hz) { _activeOscillators.RemoveAt(i); break; } 
                    } 
                }
                await SendEmuCommand("remove_freq", hz);
            } 
        }
        private async void ClearEmu_Click(object sender, RoutedEventArgs e) { lock(_oscLock) { _activeOscillators.Clear(); } await SendEmuCommand("clear"); }
        private async void EmuNoise_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!IsLoaded) return; await SendEmuCommand("set_noise", 0, e.NewValue); }

        private async void EmuGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { 
            if (!IsLoaded) return; 
            if (EmuGainValueText != null) EmuGainValueText.Text = $"{e.NewValue:F1} x";
            await SendConfigToEngine(); 
            await SendEmuCommand("set_gain", 0, e.NewValue);
        }

        private async Task SendConfigToEngine()
        {
            try {
                await _wsLock.WaitAsync(_cts.Token);
                if (_webSocket != null && _webSocket.State == WebSocketState.Open) {
                    double th = 150, low = 1, high = 40, gain = 1.0;
                    Dispatcher.Invoke(() => { 
                        th = ThresholdSlider.Value; 
                        low = LowCutSlider.Value; 
                        high = HighCutSlider.Value; 
                        gain = EmuGainSlider.Value;
                    });
                    var cfg = new { type = "config", threshold = th, low_cut = low, high_cut = high, global_gain = gain };
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cfg));
                    await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
                }
            } catch (Exception ex) { Debug.WriteLine("Engine Send Error: " + ex.Message); }
            finally { _wsLock.Release(); }
        }

        private async Task ReceiveDataLoopAsync()
        {
            var buffer = new byte[65536];
            while (_webSocket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var messageBuilder = new StringBuilder();
                WebSocketReceiveResult result;
                try {
                    do { result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token); messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count)); } while (!result.EndOfMessage);
                    using var doc = JsonDocument.Parse(messageBuilder.ToString());
                    if (doc.RootElement.TryGetProperty("type", out var t)) { string type = t.GetString() ?? ""; if (type == "telemetry") UpdateUI(doc.RootElement); else if (type == "status") { string statusStr = doc.RootElement.GetProperty("status").GetString() ?? ""; Dispatcher.Invoke(() => { StatusText.Text = statusStr; if (statusStr == "SEARCHING") StatusIndicator.Background = Brushes.Yellow; else if (statusStr == "CONNECTED") StatusIndicator.Background = Brushes.Green; }); } }
                } catch { break; }
            }
        }

        private double _emuTime = 0;
        private void UpdateUI(JsonElement data)
        {
            try {
                // Background thread: Parse data and prepare UI updates
                string status = data.TryGetProperty("status", out var s) ? s.GetString() ?? "OK" : "OK";
                double delta = data.TryGetProperty("delta", out var d) ? d.GetDouble() : 0;
                double theta = data.TryGetProperty("theta", out var t) ? t.GetDouble() : 0;
                double alpha = data.TryGetProperty("alpha", out var a) ? a.GetDouble() : 0;
                double beta = data.TryGetProperty("beta", out var b) ? b.GetDouble() : 0;
                double gamma = data.TryGetProperty("gamma", out var g) ? g.GetDouble() : 0;
                
                double[]? fftFreqs = null, fftPsd = null;
                if (data.TryGetProperty("fft_freqs", out var ff) && data.TryGetProperty("fft_psd", out var ps)) {
                    int len = ff.GetArrayLength(); fftFreqs = new double[len]; fftPsd = new double[len];
                    for(int i=0; i<len; i++) { fftFreqs[i] = ff[i].GetDouble(); fftPsd[i] = ps[i].GetDouble(); }
                }

                OscillatorInfo[] oscSnapshot;
                lock(_oscLock) { oscSnapshot = _activeOscillators.ToArray(); }
                var emuPoints = new System.Collections.Generic.Dictionary<string, double[]>();
                var softPoints = new System.Collections.Generic.Dictionary<string, double[]>();
                
                if (data.TryGetProperty("waves", out var waves)) {
                    double t_frame = _emuTime; // Sync all bands to the same start time
                    PrepareBandData("delta", waves, 1, 4, oscSnapshot, t_frame, emuPoints, softPoints);
                    PrepareBandData("theta", waves, 4, 8, oscSnapshot, t_frame, emuPoints, softPoints);
                    PrepareBandData("alpha", waves, 8, 12, oscSnapshot, t_frame, emuPoints, softPoints);
                    PrepareBandData("beta", waves, 12, 30, oscSnapshot, t_frame, emuPoints, softPoints);
                    PrepareBandData("gamma", waves, 30, 40, oscSnapshot, t_frame, emuPoints, softPoints);
                    if (status == "OK") _emuTime += (25.0 / 256.0); // Increment global clock
                }

                // Dispatch to UI thread for component updates
                Dispatcher.Invoke(() =>
                {
                    StatusIndicator.Background = (status == "OK" || status == "CONNECTED") ? Brushes.LimeGreen : (status == "INITIALIZING" ? Brushes.Yellow : Brushes.Red);
                    
                    if (status != "OK") {
                         // Clear metrics if the stream is dead
                        _currentDelta = 0; _currentTheta = 0; _currentAlpha = 0; _currentBeta = 0; _currentGamma = 0;
                    } else {
                        _currentDelta = delta; _currentTheta = theta; _currentAlpha = alpha; _currentBeta = beta; _currentGamma = gamma;
                    }

                    DeltaText.Text = _currentDelta.ToString("F2"); ThetaText.Text = _currentTheta.ToString("F2"); AlphaText.Text = _currentAlpha.ToString("F2"); BetaText.Text = _currentBeta.ToString("F2"); GammaText.Text = _currentGamma.ToString("F2");
                    
                    CalculateRatio();

                    if (status != "SEARCHING") {
                        ProcessEegData(data, "af7", _rawStreamAF7, _filtStreamAF7); ProcessEegData(data, "af8", _rawStreamAF8, _filtStreamAF8); ProcessEegData(data, "tp9", _rawStreamTP9, _filtStreamTP9); ProcessEegData(data, "tp10", _rawStreamTP10, _filtStreamTP10);
                        
                        ApplyBandData("delta", _softDelta, _emuDelta, emuPoints, softPoints);
                        ApplyBandData("theta", _softTheta, _emuTheta, emuPoints, softPoints);
                        ApplyBandData("alpha", _softAlpha, _emuAlpha, emuPoints, softPoints);
                        ApplyBandData("beta", _softBeta, _emuBeta, emuPoints, softPoints);
                        ApplyBandData("gamma", _softGamma, _emuGamma, emuPoints, softPoints);

                        _deltaTrend?.Add(_currentDelta); _thetaTrend?.Add(_currentTheta); _alphaTrend?.Add(_currentAlpha); _betaTrend?.Add(_currentBeta); _gammaTrend?.Add(_currentGamma);

                        if (data.TryGetProperty("new_raw_af7", out var nr)) foreach (var v in nr.EnumerateArray()) _pipeRaw?.Add(v.GetDouble());
                        if (data.TryGetProperty("new_filt_af7", out var nf)) foreach (var v in nf.EnumerateArray()) _pipeFilt?.Add(v.GetDouble());
                        if (data.TryGetProperty("new_denoised_af7", out var nd)) foreach (var v in nd.EnumerateArray()) _pipeDen?.Add(v.GetDouble());
                    }
                    
                    if (fftFreqs != null && fftPsd != null) {
                        if (_fftScatter != null) { FftPlot.Plot.Remove(_fftScatter); }
                        _fftScatter = FftPlot.Plot.Add.ScatterLine(fftFreqs, fftPsd); _fftScatter.Color = ScottPlot.Colors.Purple;
                        if (_nfFftScatter != null) { NfFftPlot.Plot.Remove(_nfFftScatter); }
                        _nfFftScatter = NfFftPlot.Plot.Add.ScatterLine(fftFreqs, fftPsd); _nfFftScatter.Color = ScottPlot.Colors.Purple;
                        if (_pipeFft != null) { PipelinePlotFft.Plot.Remove(_pipeFft); }
                        _pipeFft = PipelinePlotFft.Plot.Add.ScatterLine(fftFreqs, fftPsd); _pipeFft.Color = ScottPlot.Colors.Purple;
                    }

                    // Refresh all plots once per frame
                    EegPlotAF7.Refresh(); EegPlotAF8.Refresh(); EegPlotTP9.Refresh(); EegPlotTP10.Refresh();
                    EmuPlotDelta.Refresh(); SoftPlotDelta.Refresh(); EmuPlotTheta.Refresh(); SoftPlotTheta.Refresh();
                    EmuPlotAlpha.Refresh(); SoftPlotAlpha.Refresh(); EmuPlotBeta.Refresh(); SoftPlotBeta.Refresh();
                    EmuPlotGamma.Refresh(); SoftPlotGamma.Refresh();
                    TrendPlot.Refresh(); FftPlot.Refresh(); NfFftPlot.Refresh(); PipelinePlotRaw.Refresh(); PipelinePlotFiltered.Refresh(); PipelinePlotDenoised.Refresh(); PipelinePlotFft.Refresh();
                });
            } catch (Exception ex) { Debug.WriteLine("UI Update Error: " + ex.Message); }
        }

        private void PrepareBandData(string band, JsonElement waves, double low, double high, OscillatorInfo[] snapshot, double t, System.Collections.Generic.Dictionary<string, double[]> emuPoints, System.Collections.Generic.Dictionary<string, double[]> softPoints)
        {
            if (waves.TryGetProperty(band, out var arr)) {
                var spts = new double[arr.GetArrayLength()]; int i = 0; foreach (var v in arr.EnumerateArray()) spts[i++] = v.GetDouble();
                softPoints[band] = spts;
            }
            double[] epts = new double[25];
            for (int i = 0; i < 25; i++) {
                double val = 0; foreach (var osc in snapshot) if (osc.Hz >= low && osc.Hz < high) val += osc.Amp * Math.Sin(2 * Math.PI * osc.Hz * t);
                epts[i] = val; t += 1.0 / 256.0;
            }
            emuPoints[band] = epts;
        }

        private void ApplyBandData(string band, ScottPlot.Plottables.DataStreamer? softStream, ScottPlot.Plottables.DataStreamer? emuStream, System.Collections.Generic.Dictionary<string, double[]> emuPoints, System.Collections.Generic.Dictionary<string, double[]> softPoints)
        {
            if (softStream != null && softPoints.ContainsKey(band)) { foreach (var v in softPoints[band]) softStream.Add(v); }
            if (emuStream != null && emuPoints.ContainsKey(band)) { foreach (var v in emuPoints[band]) emuStream.Add(v); }
        }

        private void ProcessEegData(JsonElement data, string ch, ScottPlot.Plottables.DataStreamer? rS, ScottPlot.Plottables.DataStreamer? fS)
        {
            if (rS == null || fS == null) return;
            if (data.TryGetProperty($"new_raw_{ch}", out var r) && data.TryGetProperty($"new_filt_{ch}", out var f)) {
                double sum = 0; int cnt = 0; foreach (var v in r.EnumerateArray()) { sum += v.GetDouble(); cnt++; }
                double mean = cnt > 0 ? sum / cnt : 0;
                foreach (var v in r.EnumerateArray()) rS.Add(v.GetDouble() - mean);
                foreach (var v in f.EnumerateArray()) fS.Add(v.GetDouble());
            }
        }

        private void CalculateRatio() {
            if (RatioNumCombo == null || RatioDenCombo == null || RatioText == null) return;
            string n = ((ComboBoxItem)RatioNumCombo.SelectedItem)?.Content?.ToString() ?? "Alpha", d = ((ComboBoxItem)RatioDenCombo.SelectedItem)?.Content?.ToString() ?? "Beta";
            double r = GetBandValue(d) > 0 ? GetBandValue(n) / GetBandValue(d) : 0;
            RatioText.Text = r.ToString("F2"); if (FftRatioText != null) FftRatioText.Text = r.ToString("F2"); if (NfRatioText != null) NfRatioText.Text = r.ToString("F2");
            UpdateNeurofeedbackVolume(r);
        }

        private double GetBandValue(string b) => b switch { "Delta" => _currentDelta, "Theta" => _currentTheta, "Alpha" => _currentAlpha, "Beta" => _currentBeta, "Gamma" => _currentGamma, _ => 0 };
        private void UpdateNeurofeedbackVolume(double r) { if (_volumeProvider == null) return; float v = (float)(1.0 - (r / _targetRatio)) * _masterVolume; _targetVolume = v < 0 ? 0 : v > 1 ? 1 : v; }
        private void RatioCombo_SelectionChanged(object s, SelectionChangedEventArgs e) => CalculateRatio();
        private async void ThresholdSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) { if (!IsLoaded || ThresholdValueText == null) return; ThresholdValueText.Text = $"{Math.Round(e.NewValue)} uV"; await SendConfigToEngine(); }
        private void TargetRatioSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) { if (!IsLoaded || TargetRatioValueText == null) return; _targetRatio = e.NewValue; TargetRatioValueText.Text = _targetRatio.ToString("F1"); }
        private async void FilterSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) { 
            if (!IsLoaded || LowCutValueText == null || HighCutValueText == null) return;
            LowCutValueText.Text = $"{Math.Round(LowCutSlider.Value, 1)} Hz"; 
            HighCutValueText.Text = $"{Math.Round(HighCutSlider.Value, 1)} Hz"; 
            await SendConfigToEngine(); 
        }
        private void CalibrateButton_Click(object s, RoutedEventArgs e) { 
            _isCalibrating = true; _calibrationRatios.Clear(); _calibrationStartTime = DateTime.Now; CalibrateButton.IsEnabled = false; 
            CalibrationStatusText.Text = "Calibrating..."; CalibrationProgressBar.Value = 0; 
            _calibrationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; 
            _calibrationTimer.Tick += (st, et) => {
                var el = (DateTime.Now - _calibrationStartTime).TotalSeconds; CalibrationProgressBar.Value = el; 
                if (!_isCalibrating) return;
                CalibrationStatusText.Text = $"Calibrating ({Math.Round(el)}/60s)...";
                if (el >= 10 && el <= 50 && double.TryParse(RatioText.Text, out double r)) _calibrationRatios.Add(r);
                if (el >= 60) { 
                    _calibrationTimer?.Stop(); _isCalibrating = false; CalibrateButton.IsEnabled = true; 
                    if (_calibrationRatios.Count > 0) { double avg = 0; foreach (var v in _calibrationRatios) avg += v; avg /= _calibrationRatios.Count; TargetRatioSlider.Value = avg; CalibrationStatusText.Text = $"Done: {avg:F2}"; } 
                    else CalibrationStatusText.Text = "Failed."; 
                }
            }; _calibrationTimer.Start(); 
        }
        private void AudioEnable_Checked(object s, RoutedEventArgs e) => _waveOut?.Play();
        private void AudioEnable_Unchecked(object s, RoutedEventArgs e) => _waveOut?.Pause();
        private void BrowseAudio_Click(object s, RoutedEventArgs e) { var ofd = new OpenFileDialog { Filter = "Audio|*.mp3;*.wav" }; if (ofd.ShowDialog() == true) { try { bool was = _waveOut?.PlaybackState == PlaybackState.Playing; _waveOut?.Stop(); _waveOut?.Dispose(); _waveOut = new WaveOutEvent(); _volumeProvider = new VolumeSampleProvider(new LoopingSampleProvider(new AudioFileReader(ofd.FileName))) { Volume = _targetVolume }; _waveOut.Init(_volumeProvider); if (was) _waveOut.Play(); if (LoadedAudioText != null) LoadedAudioText.Text = System.IO.Path.GetFileName(ofd.FileName); } catch (Exception ex) { MessageBox.Show(ex.Message); } } }
        private void MasterVolumeSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) => _masterVolume = (float)e.NewValue;

        private async void KillPython_Click(object s, RoutedEventArgs e)
        {
            try {
                AppendToConsole("Force killing all python processes...");
                foreach (var p in Process.GetProcessesByName("python"))
                {
                    try { p.Kill(true); } catch { }
                }
                await Task.Delay(1000);
                AppendToConsole("Cleanup complete.");
            } catch (Exception ex) { AppendToConsole("Kill Error: " + ex.Message); }
        }

        private Process? _engineProc, _emuProc;
        private void AppendToConsole(string t) => Dispatcher.InvokeAsync(() => { ConsoleOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {t}{Environment.NewLine}"); ConsoleOutput.ScrollToEnd(); });
        private async void LaunchEngine_Click(object s, RoutedEventArgs e) { 
            if (_engineProc != null && !_engineProc.HasExited) {
                try { _engineProc.Kill(true); AppendToConsole("Stopped existing Engine."); await Task.Delay(500); } catch { }
            }
            _engineProc = LaunchPythonScript("engine.py"); 
        }
        private async void LaunchEmulator_Click(object s, RoutedEventArgs e) { 
            if (_emuProc != null && !_emuProc.HasExited) {
                try { _emuProc.Kill(true); AppendToConsole("Stopped existing Emulator."); await Task.Delay(500); } catch { }
            }
            _emuProc = LaunchPythonScript("emulator.py"); 
            _ = ConnectToEmulatorAsync(); // Connect to command socket
        }
                        private Process? LaunchPythonScript(string fn) { try { string baseDir = AppDomain.CurrentDomain.BaseDirectory; string[] ps = { Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\Backend\PyApp\" + fn)), Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\src\Backend\PyApp\" + fn)), Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"src\Backend\PyApp\" + fn)), Path.GetFullPath(@"src\Backend\PyApp\" + fn) }; string sp = ""; foreach (var p in ps) if (File.Exists(p)) { sp = p; break; } if (!string.IsNullOrEmpty(sp)) { AppendToConsole("Launching: " + sp); var p = new Process(); p.StartInfo = new ProcessStartInfo("cmd.exe", "/k python -u \"" + sp + "\"") { UseShellExecute = true, CreateNoWindow = false, WorkingDirectory = Path.GetDirectoryName(sp) ?? "" }; p.Start(); return p; } else { AppendToConsole("Could not find script: " + fn); } } catch (Exception ex) { AppendToConsole(ex.Message); } return null; }
          private void LaunchBlueMuse_Click(object s, RoutedEventArgs e) { try { AppendToConsole("Attempting to launch BlueMuse via URI..."); Process.Start(new ProcessStartInfo("bluemuse://start?streamfirst=true") { UseShellExecute = true }); } catch (Exception ex) { AppendToConsole(ex.Message); } }
                private void LaunchLabRecorder_Click(object s, RoutedEventArgs e) { try { 
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] ps = { 
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\Backend\Tools\LabRecorder\LabRecorder.exe")), 
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\src\Backend\Tools\LabRecorder\LabRecorder.exe"))
            };
            string p = ""; foreach (var path in ps) if (File.Exists(path)) { p = path; break; }

            if (!string.IsNullOrEmpty(p)) Process.Start(new ProcessStartInfo(p) { WorkingDirectory = Path.GetDirectoryName(p) }); 
            else AppendToConsole("Not found: LabRecorder.exe"); 
        } catch (Exception ex) { AppendToConsole(ex.Message); } }
        private void Window_Closing(object s, System.ComponentModel.CancelEventArgs e)
        {
            _cts.Cancel();
            _waveOut?.Stop();
            _waveOut?.Dispose();

            // Try clean shutdown
            _ = Task.Run(async () => {
                await SendEmuCommand("quit");
                try {
                    await _wsLock.WaitAsync(100);
                    if (_webSocket != null && _webSocket.State == WebSocketState.Open) {
                        var cfg = new { type = "quit" };
                        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cfg));
                        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                } catch { } finally { try { _wsLock.Release(); } catch { } }

                await Task.Delay(500); // Give them a moment to exit
                foreach (var p in new[] { _engineProc, _emuProc }) if (p != null && !p.HasExited) try { p.Kill(true); } catch { }
            });
        }
    }
}


