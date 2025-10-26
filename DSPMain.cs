using DSP_Test;
using NAudio.Dsp;
using NAudio.Wave;
using NWaves.Operations;
using NWaves.Signals;
using NWaves.Transforms;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Timers;
using System.Windows.Markup;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Complex = NAudio.Dsp.Complex;
using Graphics = System.Drawing.Graphics;
using MessageBox = System.Windows.Forms.MessageBox;
using MessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using MessageBoxIcon = System.Windows.Forms.MessageBoxIcon;
using Pens = System.Drawing.Pens;
using Point = System.Drawing.Point;

namespace DSP_General
{
    public enum DSPMode
    {
        Wave,
        FFT,
        APT
    }

    public enum DSPModulation
    {
        None,
        AM,
        APT_AM
    }

    /// <summary>
    /// 数字信号处理
    /// </summary>
    public class DSPMain
    {
        //Audio
        private WaveFileReader? reader;
        private WaveOut? wOut;
        private WasapiLoopbackCapture? capture;
        public string audioFile = "";
        public MainWindow mainWindow;

        //Visualizer
        private SpectrumGraphicDrawer drawer;
        private Point[] points;
        private Font font = new Font("Arial", 8);
        private SolidBrush brush = new SolidBrush(Color.Black);
        //Signal process
        /*Main*/
        private float[] inputSignal;
        public DSPMode mode = DSPMode.Wave;
        public DSPModulation modulation = DSPModulation.None;
        public bool enableDemodulateAPT = false;
        /*FFT Spectrum*/
        public Fft fft;
        public RealFft rFft;
        float[] fftRe;
        float[] fftIm;
        public const int FFT_SIZE = 8192;
        public const int FFT_POS = 100;
        public const int FFT_RANGE = 550;
        public const float FFT_SAMPLE2FREQ = 23.4f;
        public List<double[]> fftDataset = new List<double[]>();
        public Bitmap spectrumCacheBitmap;

        //AM Demodulation
        private HilbertTransform hilbertTransform = new HilbertTransform(1024);
        private float lastValue = 0;
        public const int hilbertTransformBufferSize = 1024;
        private float aptMax = -100;
        private float aptMin = 100;
        private float aptDelta = 0;
        //private float averageDC = -1;

        //APT Demodulation
        //false => 0, true => 255
        public readonly bool[] APT_SYNC_A = 
        [
            false, false, false,
            true, true, false, false, 
            true, true, false, false, 
            true, true, false, false,
            true, true, false, false, 
            true, true, false, false, 
            true, true, false, false,
            true, true, false, false,
            false, false, false, false,
            false, false,
        ];
        public const int APT_SAMPLE_RATE = 4160;
        public const int APT_WIDTH = 2080;
        public const int APT_CARRIER_FREQ = 2400;
        public const int APT_SR_MULTIPLE = 5;
        public const int APT_PIXEL_MERGE = 4 * 5;
        public const int APT_SLOPE_FIX = 0; // 5 * 5
        public const int APT_PIXEL_INDEX = 40;
        private Resampler resampler = new Resampler();
        private Bitmap aptImage;
        private int bitmapPixelIndex = 0;
        private float syncV = 1000;
        private List<APTPixelData> syncPixelBuffer = new List<APTPixelData>();
        private float[] remainedSample;
        private ConcurrentQueue<float[]> sampleHandleQueue = new ConcurrentQueue<float[]>();

        APTPixelData syncPixel;

        //Analyzer
        private bool updateSamplesCount = false;
        private float totalSamples = 0;
        private float measuredSamples = 0;
        private System.Timers.Timer timer = new System.Timers.Timer();
        private Stopwatch stopwatch = new Stopwatch();

        //private float[] aptSampleBuffer;
        //private int aptBufferIndex = 0;
        //private int row = 0;

        /// <summary>
        /// 构造DSPMain
        /// </summary>
        /// <param name="drawer">绘图器</param>
        /// <param name="window">主窗口</param>
        public DSPMain(SpectrumGraphicDrawer drawer, MainWindow window)
        {
            mainWindow = window;
            this.drawer = drawer;

            fft = new Fft(FFT_SIZE);
            rFft = new RealFft(FFT_SIZE);
            spectrumCacheBitmap = new Bitmap(550, 1);

            InitAnalyzer();
        }

        /// <summary>
        /// 处理采样
        /// </summary>
        /// <param name="samples">原始采样</param>
        /// <param name="sender">捕获事件的sender</param>
        private void ProcessSamples(float[] samples, int sampleRate)
        {
            //APT 调制解调
            if (enableDemodulateAPT)
            {
                //因为对于零长度数组之类的输入采样会导致报错,所以使用try
                try
                {
                    stopwatch.Start();

                    int oriSampleRate = sampleRate;

                    //重采样到APT_SAMPLE_RATE Resample sample rate to APT_SAMPLE_RATE value.
                    //DiscreteSignal signal = new DiscreteSignal(oriSampleRate, FastDemodulateAptAm(samples, APT_CARRIER_FREQ, oriSampleRate), true);
                    DiscreteSignal signal = new DiscreteSignal(oriSampleRate, DemodulateAM(samples), true);
                    float[] resampledSignal = resampler.Resample(signal, APT_SAMPLE_RATE * APT_SR_MULTIPLE).Samples;
                    //斜度修正 Slope correction.
                    float[] aptSamples = new float[resampledSignal.Length + APT_SLOPE_FIX + remainedSample.Length];
                    if (remainedSample.Length != 0)
                    {
                        Array.Copy(remainedSample, 0, aptSamples, 0, remainedSample.Length);
                        Array.Copy(resampledSignal, 0, aptSamples, remainedSample.Length - 1, resampledSignal.Length);
                    }
                    else
                    {
                        Array.Copy(resampledSignal, 0, aptSamples, 0, resampledSignal.Length);
                    }

                    ////斜度修复像素点修正 Pixel color correection of slope correction
                    //for (int i = 0; i < APT_SLOPE_FIX; i++)
                    //{
                    //    aptSamples[^(i + 1)] = resampledSignal[^APT_PIXEL_INDEX];
                    //}

                    ////采样区头尾像素修正 Start & end part pixel color corrention of samples
                    //for (int i = 0; i < APT_PIXEL_MERGE; i++)
                    //{
                    //    aptSamples[i] = aptSamples[APT_PIXEL_MERGE + APT_PIXEL_INDEX];
                    //    aptSamples[i+1] = aptSamples[APT_PIXEL_MERGE + APT_PIXEL_INDEX];
                    //    //aptSamples[^(i + 1)] = aptSamples[^(APT_PIXEL_MERGE + APT_PIXEL_INDEX)];
                    //}

                    //获取输入信号最大最小值及差值 Get max, min and delta value of input signal.
                    for (int i = 0; i < aptSamples.Length; i++)
                    {
                        if (aptSamples[i] > aptMax)
                            aptMax = aptSamples[i];
                        if (aptSamples[i] < aptMin)
                            aptMin = aptSamples[i];
                        aptDelta = Math.Abs(aptMax - aptMin);
                    }

                    int pixelCount = aptSamples.Length / APT_PIXEL_MERGE;
                    int avalibleSampleCount = pixelCount * APT_PIXEL_MERGE;
                    remainedSample = new float[aptSamples.Length - avalibleSampleCount];
                    Array.Copy(aptSamples, avalibleSampleCount, remainedSample, 0, remainedSample.Length);
                    float[] avalibleSamples = aptSamples[0..avalibleSampleCount];

                    aptSamples = avalibleSamples;

                    //信号处理 Signal processing
                    for (int i = 0; i < aptSamples.Length; i += APT_PIXEL_MERGE)
                    {
                        //灰度设置为整形最小值 Set grayscale to int.MinValue
                        int pixelGrayscale = (int)aptMin;
                        float avgSample = aptMin;

                        //防止差值是0 Avoid delta value euqals zero.
                        if (aptDelta == 0)
                            aptDelta = 0.1f; //What the fuck

                        //防止超出范围 Avoid index values that exceed the sampled array
                        if (i > aptSamples.Length - 1)
                        {
                            continue;
                        }
                        else
                        {
                            //对于APT_PIXEL_MERGE的均值计算 Average value calcucate for APT_PIXEL_MERGE
                            //float avgSample = 0;
                            int sumTimes = 0;
                            for (int j = 0; j < APT_PIXEL_MERGE; j++)
                            {
                                if (i + j < aptSamples.Length - 1)
                                {
                                    sumTimes++;
                                    avgSample += aptSamples[i + j];
                                }
                            }
                            if (sumTimes != 0)
                                avgSample /= sumTimes;

                            //灰度计算 Calcucate grayscale
                            //avgSample *= 65535;
                            //if (avgSample > 65535)
                            //    avgSample = 65535;
                            //else if (avgSample < 0)
                            //    avgSample = 0;
                            //pixelGrayscale = (int)(avgSample / 65535 * 255);
                            //pixelGrayscale = (int)avgSample;
                        }

                        //灰度计算 Calcucate grayscale
                        //float sample = aptSamples[i];
                        pixelGrayscale = Math.Clamp((int)((avgSample - aptMin) / aptDelta * 255), 0, 255);

                        //像素位置计算 Calcucate pixel position
                        int index = bitmapPixelIndex + i / APT_PIXEL_MERGE;
                        int x = index % APT_WIDTH;
                        int y = index / APT_WIDTH;

                        syncPixelBuffer.Add(new APTPixelData(x, y, pixelGrayscale));

                        //对不超出图像的像素着色 Color the pixels inside the image
                        if (x <= APT_WIDTH && y <= 1000)
                            aptImage.SetPixel(x, y, Color.FromArgb(pixelGrayscale, pixelGrayscale, pixelGrayscale));

                        //if (i + APT_PIXEL_MERGE >= aptSamples.Length)
                        //{
                        //    if (x <= APT_WIDTH && y <= 1000)
                        //        aptImage.SetPixel(x, y, Color.FromArgb(0, 0, pixelGrayscale));
                        //}
                    }

                    //信号同步
                    //同步点检测
                    if (syncPixelBuffer.Count > APT_WIDTH + APT_SYNC_A.Length)
                    {
                        APTPixelData[] temp = syncPixelBuffer[APT_WIDTH..syncPixelBuffer.Count].ToArray();
                        APTPixelData[] pixels = syncPixelBuffer[0..(APT_WIDTH + APT_SYNC_A.Length)].ToArray();
                        syncPixelBuffer.Clear();
                        syncPixelBuffer.AddRange(temp);

                        for (int i = 0; i < pixels.Length - APT_SYNC_A.Length; i++)
                        {
                            float sum = 0;
                            for (int j = 0; j < APT_SYNC_A.Length; j++)
                            {
                                APTPixelData currentPixel = pixels[i + j];
                                sum += Math.Abs((APT_SYNC_A[j] ? 255 : 0) - currentPixel.grayscale);
                            }
                            sum /= APT_SYNC_A.Length;
                            if (Math.Abs(sum) < Math.Abs(syncV))
                            {
                                syncV = sum;
                                syncPixel = pixels[i];
                            }
                        }

                        if (syncPixel.x <= APT_WIDTH && syncPixel.y <= 1000)
                        {
                            aptImage.SetPixel(syncPixel.x, syncPixel.y, Color.FromArgb(0, 255, 0));
                            //同步图像
                            int[] pixelGrayscales = new int[APT_WIDTH];
                            int currentX = syncPixel.x;
                            for (int j = 0; j < APT_WIDTH; j++)
                            {
                                pixelGrayscales[j] = aptImage.GetPixel(currentX, syncPixel.y).R;
                                currentX++;
                                if (currentX >= APT_WIDTH)
                                    currentX -= APT_WIDTH;
                            }
                            for (int j = 0; j < pixelGrayscales.Length; j++)
                            {
                                int grayscaleOfPixel = pixelGrayscales[j];
                                aptImage.SetPixel(j, syncPixel.y, Color.FromArgb(grayscaleOfPixel, grayscaleOfPixel, grayscaleOfPixel));
                            }
                        }
                        syncV = 1000;
                    }

                    /*
                    for (int i = 0; i < syncHead.Count; i++)
                    {
                        APTPixelData pixel = syncHead.Dequeue();
                        int[] pixelGrayscale = new int[APT_WIDTH];
                        for(int j = 0; j < APT_WIDTH; j++)
                        {
                            //R=G=B
                            pixelGrayscale[j] = aptImage.GetPixel(j, pixel.y).R;
                        }
                        for(int j = 0; j < APT_WIDTH; j++)
                        {
                            int index = pixel.x + j;
                            if (index > pixelGrayscale.Length - 1)
                                index -= pixelGrayscale.Length -1;
                            aptImage.SetPixel(j, pixel.y, Color.FromArgb(pixelGrayscale[index], pixelGrayscale[index], pixelGrayscale[index]));
                        }
                    }
                    */

                    //更新像素索引 Update pixel index
                    bitmapPixelIndex += aptSamples.Length / APT_PIXEL_MERGE;

                    //更新分析器 Update analyzer
                    if (updateSamplesCount)
                    {
                        updateSamplesCount = false;
                        measuredSamples = totalSamples;
                        totalSamples = 0;
                    }

                    stopwatch.Stop();
                    totalSamples += aptSamples.Length;
                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        mainWindow.timerLabel.Content =
                        $"Process time(ms): {stopwatch.ElapsedMilliseconds}\n" +
                        $"Sample size: {aptSamples.Length} ({(float)aptSamples.Length / APT_PIXEL_MERGE / APT_WIDTH}% line)\n" +
                        $"Pixel pos:(x:{bitmapPixelIndex % APT_WIDTH}, y: {bitmapPixelIndex / APT_WIDTH})\n" +
                        $"--- 1s Analyze ---\n" +
                        $"Total samples: {measuredSamples} ({measuredSamples / APT_PIXEL_MERGE / APT_WIDTH} lines)\n" +
                        $"--- Sync Analyze ---\n" +
                        $"Sync point:({syncPixel.x}, {syncPixel.y})";
                    });
                    stopwatch.Reset();
                }
                catch(Exception)
                {
                    //...
                }
            }

            //调制模式
            switch (modulation)
            {
                case DSPModulation.None:
                    inputSignal = samples;
                    break;

                //AM Demodulation
                case DSPModulation.AM:

                    float[] demodulatedSignalAM = DemodulateAM(samples);
                    inputSignal = demodulatedSignalAM;
                    break;

                case DSPModulation.APT_AM:
                    float[] demodulatedSignalAPT = FastDemodulateAptAm(samples, APT_CARRIER_FREQ, capture.WaveFormat.SampleRate);
                    inputSignal = demodulatedSignalAPT;
                    break;
            }

            //分析模式
            switch (mode)
            {
                case DSPMode.Wave:
                    int channelCount = capture.WaveFormat.Channels;   // WasapiLoopbackCapture 的 WaveFormat 指定了当前声音的波形格式, 其中包含就通道数
                    float[][] channelSamples = Enumerable
                        .Range(0, channelCount)
                        .Select(channel => Enumerable
                            .Range(0, inputSignal.Length / channelCount)
                            .Select(i => inputSignal[channel + i * channelCount])
                            .ToArray())
                        .ToArray();

                    float[] averageSamples = Enumerable
                        .Range(0, inputSignal.Length / channelCount)
                        .Select(index => Enumerable
                        .Range(0, channelCount)
                        .Select(channel => channelSamples[channel][index])
                        .Average())
                        .ToArray();

                    points = averageSamples
                        .Select((v, i) => new Point((int)(i * ((float)MainWindow.sWidth / averageSamples.Length)), MainWindow.sHeight / 2 - (int)(v * 100)))
                        .ToArray();   // 将数据转换为坐标点

                    drawer.Draw((graphics) =>
                    {
                        graphics.DrawLines(Pens.Black, points);   // 连接这些点, 画线
                    });
                    break;

                case DSPMode.FFT:
                    //int log = (int)Math.Ceiling(Math.Log(inputSignal.Length, 2));
                    //float[] filledSamples = new float[(int)Math.Pow(2, log)];
                    //Array.Copy(inputSignal, filledSamples, inputSignal.Length);

                    //Complex[] complexSrc = filledSamples.Select((v, i) =>
                    //{
                    //    double deg = i / (double)sampleRate * Math.PI * 2;
                    //    return new Complex()
                    //    {
                    //        X = (float)(Math.Cos(deg) * v),
                    //        Y = (float)(Math.Sin(deg) * v)
                    //    };
                    //}).ToArray();

                    //FastFourierTransform.FFT(false, log, complexSrc);
                    //double[] result = complexSrc.Select(v => Math.Sqrt(v.X * v.X + v.Y * v.Y)).ToArray();
                    //double[] actualProcess = result;

                    //points = actualProcess
                    //    .Select((v, i) => new Point((int)(i * ((float)MainWindow.sWidth / actualProcess.Length)), MainWindow.sHeight - (int)(v * 1) - 10))
                    //    .ToArray();   // 将数据转换为一个个的坐标点

                    //drawer.Draw((graphics) =>
                    //{
                    //    graphics.DrawLines(Pens.Black, points);   // 连接这些点, 画线
                    //    for (int i = 0; i < actualProcess.Length; i += 100)
                    //    {
                    //        if (i < actualProcess.Length - 1)
                    //        {
                    //            int xPos = (int)(i * ((float)MainWindow.sWidth / actualProcess.Length));
                    //            graphics.DrawLine(Pens.Black, new Point(xPos, MainWindow.sHeight), new Point(xPos, MainWindow.sHeight - 5));
                    //        }
                    //    }

                    //});

                    if (inputSignal.Length > 0 && inputSignal.Length > FFT_SIZE)
                    {
                        fftIm = new float[FFT_SIZE];
                        fftRe = new float[FFT_SIZE];
                        rFft.Direct(inputSignal, fftRe, fftIm);
                        double[] result = fftIm.Select((v, i) => Math.Sqrt(v * v + fftRe[i] * fftRe[i])).ToArray();
                        //fft.Direct(inputSignal[0..Math.Min(FFT_SIZE, inputSignal.Length)], fftIm);
                        //double[] result = fftIm.Select((v, i) => Math.Sqrt(v * v + inputSignal[i] * inputSignal[i])).ToArray();
                        double[] actualProcess = result[0..FFT_RANGE];
                        if(fftDataset.Count - 1 >= FFT_POS)
                        {
                            fftDataset.RemoveAt(0);
                        }
                        fftDataset.Add(actualProcess);

                        points = actualProcess
                            .Select((v, i) => new Point((int)(i * ((float)MainWindow.sWidth / actualProcess.Length)), MainWindow.sHeight - (int)(v * 2) - FFT_POS))
                            .ToArray();   // 将数据转换为一个个的坐标点

                        drawer.Draw((graphics) =>
                        {
                            graphics.DrawLines(Pens.Black, points);   // 连接这些点, 画线

                            //GUI
                            graphics.DrawString($"RealFFT[{FFT_SIZE}](Visualizing index range: 0 - 550)\nInput Signal[{inputSignal.Length}]\nOutput RealFFT[{result.Length}]", font, brush, new Point(5, 5));
                            graphics.DrawLine(Pens.Black, new Point(0, MainWindow.sHeight - FFT_POS), new Point(MainWindow.sWidth, MainWindow.sHeight - FFT_POS));
                            for (int i = 0; i < fftDataset.Count; i++)
                            {
                                double[] data = fftDataset[^(i+1)];
                                for (int j = 0; j < data.Length; j++)
                                {
                                    int value = (int)Math.Clamp(data[j] * 5, 0, 255);
                                    spectrumCacheBitmap.SetPixel(j, 0, Color.FromArgb(value, 0, 255 / 4));
                                }
                                graphics.DrawImage(spectrumCacheBitmap, 0, MainWindow.sHeight - (FFT_POS - 20) + i, MainWindow.sWidth, 1);
                            }

                            for (int i = 0; i < actualProcess.Length; i += 100)
                            {
                                if (i < actualProcess.Length - 1)
                                {
                                    int xPos = (int)(i * ((float)MainWindow.sWidth / actualProcess.Length));
                                    graphics.DrawLine(Pens.Black, new Point(xPos, MainWindow.sHeight - FFT_POS + 5), new Point(xPos, MainWindow.sHeight - FFT_POS));
                                    graphics.DrawString($"{i*FFT_SAMPLE2FREQ}Hz", font, brush, new Point(xPos, MainWindow.sHeight - FFT_POS + 5));
                                }
                            }

                        });
                    }

                    break;

                case DSPMode.APT:
                    drawer.Draw((graphics) =>
                    {
                        //graphics.DrawImage(aptImage, 0, 0, MainWindow.sWidth, MainWindow.sHeight);
                        graphics.DrawImage(aptImage, 0, 0, MainWindow.sWidth, MainWindow.sWidth * (1000f / APT_WIDTH));
                    });

                    break;
            }
        }

        #region 调制解调
        /// <summary>
        /// 快速解调AM(针对APT, 即采样率远大于载波频率)
        /// </summary>
        /// <param name="samples">输入采样</param>
        /// <param name="CARRIER_FREQ">载波频率</param>
        /// <param name="SAMPLE_RATE">采样率</param>
        /// <returns>解调后采样</returns>
        public float[] FastDemodulateAptAm(float[] samples, float CARRIER_FREQ, int SAMPLE_RATE)
        {
            float[] output = new float[samples.Length];
            float theta = (float)(2 * Math.PI * CARRIER_FREQ / SAMPLE_RATE);

            for (int i = 0; i < samples.Length; i++)
            {
                float curSample = samples[i];
                if (i != 0)
                {
                    if (i == samples.Length - 1)
                    {
                        lastValue = samples[i];
                    }
                    float lastSample = samples[i - 1];
                    output[i] = (float)(Math.Sqrt(curSample * curSample + lastSample * lastSample - 2 * curSample * lastSample * Math.Cos(theta)) / Math.Sin(theta));
                }
                else
                {
                    float lastSample = lastValue;
                    output[i] = (float)(Math.Sqrt(curSample * curSample + lastSample * lastSample - 2 * curSample * lastSample * Math.Cos(theta)) / Math.Sin(theta));
                }
            }

            //throw new Exception("test");
            return output;
        }

        /// <summary>
        /// 用希尔伯特变换解调APT
        /// </summary>
        /// <param name="samples">输入采样</param>
        /// <returns>解调后采样</returns>
        public float[] DemodulateAM(float[] samples)
        {
            float[] demodulatedSignalAM = new float[samples.Length];
            ComplexDiscreteSignal complexSignal;
            List<float[]> subArrays = GroupArrayBySize(samples, hilbertTransformBufferSize);

            for (int i = 0; i < subArrays.Count; i++)
            {
                //希尔伯特变换
                complexSignal = hilbertTransform.AnalyticSignal(subArrays[i]);
                float[] demodulatedSignalArr = new float[complexSignal.Length];
                //取模
                for (int j = 0; j < complexSignal.Length; j++)
                {
                    double re = complexSignal.Real[j];
                    double im = complexSignal.Imag[j];
                    demodulatedSignalArr[j] = (float)Math.Sqrt(re * re + im * im);
                }
                //合并
                if (i != 0)
                    Array.Copy(demodulatedSignalArr, 0, demodulatedSignalAM, i * hilbertTransformBufferSize - 1, i * hilbertTransformBufferSize + demodulatedSignalArr.Length > demodulatedSignalAM.Length ? demodulatedSignalAM.Length - i * hilbertTransformBufferSize : demodulatedSignalArr.Length);
                else
                    Array.Copy(demodulatedSignalArr, 0, demodulatedSignalAM, 0, demodulatedSignalArr.Length);
            }

            return demodulatedSignalAM;

            //if(averageDC == -1)
            //{
            //    averageDC = demodulatedSignalAM.Average();
            //}

            //return demodulatedSignalAM.Select(v => v - averageDC).ToArray();
        }
        #endregion

        #region 信号处理相关
        /// <summary>
        /// 初始化分析器
        /// </summary>
        public void InitAnalyzer()
        {
            timer.Interval = 1000;
            timer.Elapsed += (object? sender, ElapsedEventArgs e) => { updateSamplesCount = true; };
        }
        /// <summary>
        /// 初始化APT解调
        /// </summary>
        public void InitAPTDemodulation()
        {
            bitmapPixelIndex = 0;
            if (aptImage != null)
            {
                aptImage.Dispose();
            }
            aptImage = new Bitmap(APT_WIDTH, 1000);
            Graphics graphics = Graphics.FromImage(aptImage);
            graphics.Clear(Color.White);
            graphics.Flush();
            graphics.Dispose();

            syncPixelBuffer.Clear();
            remainedSample = Array.Empty<float>();
            sampleHandleQueue.Clear();
        }
        
        /// <summary>
        /// 保存解码图像到(我的)桌面
        /// </summary>
        public void SaveAPTImage()
        {
            string aptPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\apt-output";
            Directory.CreateDirectory(aptPath);
            SaveAPTImage(@$"{aptPath}\apt_img.png");
        }

        /// <summary>
        /// 保存解码图像
        /// </summary>
        /// <param name="filename">路径</param>
        public void SaveAPTImage(string filename)
        {
            aptImage.Save(filename);
        }

        /// <summary>
        /// 初始化音频捕获
        /// </summary>
        public void InitCapture()
        {
            capture = new WasapiLoopbackCapture();
            capture.DataAvailable += (sender, e) =>
            {
                float[] samples = Enumerable
                .Range(0, e.BytesRecorded / 4)
                .Select(i => BitConverter.ToSingle(e.Buffer, i * 4))
                .ToArray();

                ProcessSamples(samples, ((WasapiLoopbackCapture)sender).WaveFormat.SampleRate);
            };
            //aptSampleBuffer = new float[capture.WaveFormat.SampleRate / 2];
        }

        /// <summary>
        /// 开始音频捕获
        /// </summary>
        public void StartCapture()
        {
            try
            {
                if (capture != null)
                    capture.StartRecording();
                else
                {
                    //它不会空引用因为InitCapture函数会在capture为空时实例化它的对象
#pragma warning disable CS8602
                    InitCapture();
                    capture.StartRecording();
#pragma warning restore CS8602
                }
            }
            catch(Exception)
            {
                MessageBox.Show("出现错误或者监听已启动", "不能启动监听", MessageBoxButtons.OK, MessageBoxIcon.Question);
            }
        }

        /// <summary>
        /// 停止音频捕获
        /// </summary>
        public void StopCapture()
        {
            try
            {
                if (capture != null)
                    capture.StopRecording();
                else
                    InitCapture();
            }
            catch(Exception)
            {
                MessageBox.Show("出现错误或者监听还未启动", "不能停止监听", MessageBoxButtons.OK, MessageBoxIcon.Question);
            }
        }

        /// <summary>
        /// 释放音频捕获
        /// </summary>
        public void DisposeCapture()
        {
            if (capture != null)
                capture.Dispose();
        }

        /// <summary>
        /// 开始APT解调分析计时器
        /// </summary>
        public void StartTimer()
        {
            timer.Enabled = true;
            timer.Start();
        }

        #endregion

        #region 音频播放
        public void Load()
        {
            try
            {
                reader = new WaveFileReader(audioFile);
                MessageBox.Show("加载完成", "音频流加载");

                wOut = new WaveOut();
                wOut.Init(reader);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 播放音频
        /// </summary>
        public void Play()
        {
            if (wOut != null && wOut.PlaybackState == PlaybackState.Stopped)
                wOut.Play();
            else if (wOut != null && wOut.PlaybackState == PlaybackState.Paused)
                wOut.Resume();
            else
                MessageBox.Show("'wOut'为空, 请确认音频是否已加载", "不能播放音频", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// 暂停音频
        /// </summary>
        public void Pause()
        {
            if (wOut != null)
                wOut.Pause();
            else
                MessageBox.Show("'wOut'为空, 请确认音频是否已加载", "不能暂停音频", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// 重新开始音频
        /// </summary>
        public void Restart()
        {
            if (wOut != null)
                wOut.Play();
            else
                MessageBox.Show("'wOut'为空, 请确认音频是否已加载", "不能播放音频", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// 结束音频
        /// </summary>
        public void Stop()
        {
            if (wOut != null)
                wOut.Stop();
            else
                MessageBox.Show("'wOut'为空, 请确认音频是否已加载", "不能停止音频", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// 获取音频播放进度(0-1)
        /// </summary>
        /// <returns>进度</returns>
        public double GetProgress()
        {
            if (wOut != null && reader != null)
                return reader.CurrentTime.TotalSeconds / reader.TotalTime.TotalSeconds;
            else
                return 0;
        }
        #endregion

        #region Utils
        /// <summary>
        /// 根据大小分割数组
        /// </summary>
        /// <param name="list">原数组</param>
        /// <param name="size">要分割成的大小</param>
        /// <returns>分割后数组</returns>
        private List<float[]> GroupArrayBySize(float[] list, int size)
        {
            List<float[]> listArr = new List<float[]>();
            int arrSize = list.Length % size == 0 ? list.Length / size : list.Length / size + 1;
            for (int i = 0; i < arrSize; i++)
            {
                //float[] sub = new float[]();
                //for (int j = i * size; j <= size * (i + 1) - 1; j++)
                //{
                //    if (j <= list.Count() - 1)
                //    {
                //        sub.Add(list[j]);
                //    }
                //}
                //listArr.Add(sub);

                int index = i * size;
                float[] subary = list.Skip(index).Take(size).ToArray();
                listArr.Add(subary);
            }
            return listArr;
        }

        public float GetDeltaValue(float valueA, float valueB)
        {
            return Math.Abs(valueA - valueB);
        }
        #endregion
    }
}
