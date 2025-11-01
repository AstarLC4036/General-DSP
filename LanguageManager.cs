using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Globalization;
using System;

namespace DSP_General
{
    public static class LanguageManager
    {
        private static ResourceDictionary currentLanguage;

        public static ResourceDictionary CurrentLanguage
        {
            get { return currentLanguage; }
            set
            {
                if (currentLanguage != value)
                {
                    currentLanguage = value;
                    UpdateLanguage();
                }
            }
        }

        private static void UpdateLanguage()
        {
            if (Application.Current.Resources.MergedDictionaries.Contains(currentLanguage))
            {
                Application.Current.Resources.MergedDictionaries.Remove(currentLanguage);
                Application.Current.Resources.MergedDictionaries.Add(currentLanguage);
            }
            else
            {
                Application.Current.Resources.MergedDictionaries.Add(currentLanguage);
            }
        }
    }
}
