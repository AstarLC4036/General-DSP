using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DSP_General
{
    public struct APTPixelData
    {
        public int x;
        public int y;
        public int grayscale;

        public APTPixelData(int x, int y, int grayscale)
        {
            this.x = x;
            this.y = y;
            if (grayscale < 0 || grayscale > 255)
                throw new ArgumentException($"Value of 'grayscale'(${grayscale}) must be in the range of 0-255");
            this.grayscale = grayscale;
        }
    }
}
