using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;

namespace DSP_General
{
    public class SpectrumGraphicDrawer
    {
        MainWindow mainWindow;

        public SpectrumGraphicDrawer(int width, int height, MainWindow mainWindow)
        {
            this.width = width;
            this.height = height;
            this.mainWindow = mainWindow;
            drawable = new WriteableBitmap(width, height, 72, 72, PixelFormats.Bgr24, null);
        }

        public Bitmap? backBitmap;
        public Graphics? graphics;
        private WriteableBitmap drawable;
        public WriteableBitmap Drawable => drawable;

        private int width = 0;
        private int height = 0;
        public int Width => width;
        public int Height => height;

        public void Draw(Action<Graphics> drawAction)
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                {
                    mainWindow.spectrumDisplay.Width = width;
                    mainWindow.spectrumDisplay.Height = height;
                    mainWindow.spectrumDisplay.Source = drawable;
                    backBitmap = new Bitmap(width, height, drawable.BackBufferStride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, drawable.BackBuffer);

                    drawable.Lock();
                    Graphics graphics = Graphics.FromImage(backBitmap);
                    graphics.Clear(System.Drawing.Color.White);
                    try
                    {
                        drawAction(graphics);
                    }
                    catch(ArgumentException)
                    {
                        //Usually that means no audio input.
                    }
                    graphics.Flush();
                    graphics.Dispose();
                    backBitmap.Dispose();

                    drawable.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    drawable.Unlock();
                }
            });
        }
        public void SetImage(WriteableBitmap bitmap)
        {
            mainWindow.spectrumDisplay.Source = bitmap;
        }
    }
}
