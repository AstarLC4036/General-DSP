using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Image = System.Windows.Controls.Image;

namespace DSP_General
{
    public class SpectrumGraphicDrawer
    {
        MainWindow mainWindow;

        public SpectrumGraphicDrawer(int width, int height, MainWindow mainWindow)
        {
            this.mainWindow = mainWindow;
            drawable = new WriteableBitmap(width, height, 72, 72, PixelFormats.Bgr24, null);
            //mainWindow.SizeChanged += OnSizeChange;
        }

        public Bitmap? backBitmap;
        public Graphics? graphics;
        private WriteableBitmap drawable;
        public WriteableBitmap Drawable => drawable;

        public void OnSizeChange()
        {
            drawable = new WriteableBitmap(MainWindow.sWidth, MainWindow.sHeight, 72, 72, PixelFormats.Bgr24, null);
            mainWindow.spectrumDisplay.Source = drawable;
            backBitmap = new Bitmap(MainWindow.sWidth, MainWindow.sHeight, drawable.BackBufferStride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, drawable.BackBuffer);
            graphics = Graphics.FromImage(backBitmap);
        }

        public void Draw(Action<Graphics> drawAction)
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                {
                    if (drawable.Width != MainWindow.sWidth || drawable.Height != MainWindow.sHeight)
                    {
                        OnSizeChange();
                    }
                    drawable.Lock();
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

                    drawable.AddDirtyRect(new Int32Rect(0, 0, MainWindow.sWidth, MainWindow.sHeight));
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
