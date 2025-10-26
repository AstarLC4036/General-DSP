using NAudio.Wave;
using System.Drawing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
//using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MessageBox = System.Windows.Forms.MessageBox;
using MessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using MessageBoxIcon = System.Windows.Forms.MessageBoxIcon;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace DSP_General
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static MainWindow instance;
        public static MainWindow Instance
        {
            get
            {
                return instance;
            }
        }
        public SpectrumGraphicDrawer drawer;
        public DSPMain dsp;

        public static int sWidth = 734;//625
        public static int sHeight = 487;//387

        public WasapiLoopbackCapture capture = new WasapiLoopbackCapture();

        private bool requireProgessUpdate = false;

        #pragma warning disable CS8618
        public MainWindow()
        {
            instance = this;

            InitializeComponent();
            InitSpectrumDrawer();
            InitSettings();
        }
        #pragma warning restore CS8618

        private void SelectWavFile(object sender, RoutedEventArgs e)
        {
            OpenFileDialog pickWavDialog = new OpenFileDialog();
            pickWavDialog.Filter = "Wav文件|*.wav|任意文件|*.";
            if(pickWavDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                wavPathInput.Text = pickWavDialog.FileName;
            }
        }

        private void InitSpectrumDrawer()
        {
            this.SizeChanged += OnWindowSizeChange;
            //const int width = 2080;
            //const int height = 1000;

            drawer = new SpectrumGraphicDrawer(sWidth, sHeight, this);
            dsp = new DSPMain(drawer, this);
        }

        private void InitSettings()
        {
            languageBox.ItemsSource = Settings.languages;
            languageBox.SelectedIndex = Settings.languageIndex;
            Settings.ApplyLanguageSettings();
        }

        private async void LoadWav(object sender, RoutedEventArgs e)
        {
            dsp.audioFile = wavPathInput.Text;
            dsp.Load();

            requireProgessUpdate = true;
            await UpdateProgress();
        }

        private async void PlayAudio(object sender, RoutedEventArgs e)
        {
            dsp.Play();
            if(!requireProgessUpdate)
            {
                requireProgessUpdate = true;
                await UpdateProgress();
            }
        }

        private void PauseAudio(object sender, RoutedEventArgs e)
        {
            dsp.Pause();
        }

        private void StopAudio(object sender, RoutedEventArgs e)
        {
            dsp.Stop();
            dsp.Load();
            requireProgessUpdate = false;
        }

        private async Task UpdateProgress()
        {
            while(requireProgessUpdate)
            {
                this.Dispatcher.Invoke(() => { this.audioProgress.Value = this.dsp.GetProgress(); });
                await Task.Delay(100);
            }
        }

        private void OnWindowSizeChange(object sender, SizeChangedEventArgs arg)
        {
            int w = (int)spectrum.ActualWidth;
            int h = (int)spectrum.ActualHeight;

            if(w > 0 && h > 0)
            {
                sWidth = w;
                sHeight = h;
                spectrumDisplay.Width = sWidth;
                spectrumDisplay.Height = sHeight;
            }
        }

#pragma warning disable CS8629
        private void SwitchListenStatus(object sender, RoutedEventArgs e)
        {
            if((bool)listenAudio.IsChecked)
            {
                drawer.OnSizeChange();
                dsp.StartCapture();
            }
            else
            {
                dsp.StopCapture();
            }
        }

        private void WaveBoxClick(object sender, RoutedEventArgs e)
        {
            ModeCanncelAll();
            waveBox.IsChecked = true;
            dsp.mode = DSPMode.Wave;
        }

        private void FFTBoxClick(object sender, RoutedEventArgs e)
        {
            ModeCanncelAll();
            fftBox.IsChecked = true;
            dsp.mode = DSPMode.FFT;
        }

        private void APTBoxClick(object sender, RoutedEventArgs e)
        {
            ModeCanncelAll();
            aptBox.IsChecked = true;
            dsp.mode = DSPMode.APT;
        }

        private void ModeCanncelAll()
        {
            waveBox.IsChecked = false;
            fftBox.IsChecked = false;
            aptBox.IsChecked = false;
        }

        private void NoModulationClick(object sender, RoutedEventArgs e)
        {
            ModulationCancelAll();
            noModulationBox.IsChecked = true;
            dsp.modulation = DSPModulation.None;
        }

        private void AMClick(object sender, RoutedEventArgs e)
        {
            ModulationCancelAll();
            amBox.IsChecked = true;
            dsp.modulation = DSPModulation.AM;
        }

        private void AptAmClick(object sender, RoutedEventArgs e)
        {
            ModulationCancelAll();
            aptAmBox.IsChecked = true;
            dsp.modulation = DSPModulation.APT_AM;
        }

        private void ModulationCancelAll()
        {
            noModulationBox.IsChecked = false;
            amBox.IsChecked = false;
            aptAmBox.IsChecked = false;
        }

        private void SwitchAPTModeClick(object sender, RoutedEventArgs e)
        {
            dsp.enableDemodulateAPT = (bool)aptMode.IsChecked;
            if(dsp.enableDemodulateAPT)
            {
                dsp.InitAPTDemodulation();
                dsp.StartTimer();
            }
            else
            {
                dsp.SaveAPTImage();
            }
        }

        private void languageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Settings.languageIndex = languageBox.SelectedIndex;
        }

        private void SettingsApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            Settings.ApplySettings();
        }

        private void appTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (appTabs.SelectedIndex == 2)
            {
                languageBox.SelectedIndex = Settings.languageIndex; //It not works
            }
        }
#pragma warning restore CS8629
    }
}