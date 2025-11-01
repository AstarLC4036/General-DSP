using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace DSP_General
{
    public class Settings
    {
        //TODO:save settings

        public static readonly string[] languages = new string[]
        {
            "English",
            "简体中文",
            //"BD3RMM"
            "繁体中文",
            //"BG2GSX"
        };

        public static int languageIndex = 0; //English as the default language

        public static void ApplySettings()
        {
            ApplyLanguageSettings();
        }

        public static void ApplyLanguageSettings()
        {
            languageIndex = Math.Clamp(languageIndex, 0, languages.Length - 1);
            switch (languages[languageIndex])
            {
                case "简体中文":
                    LanguageManager.CurrentLanguage = new ResourceDictionary { Source = new Uri("Resources.zh-Hans.xaml", UriKind.Relative) };
                    break;
                case "English":
                    LanguageManager.CurrentLanguage = new ResourceDictionary { Source = new Uri("Resources.en-US.xaml", UriKind.Relative) };
                    break;
                case "繁体中文":
                    LanguageManager.CurrentLanguage = new ResourceDictionary { Source = new Uri("Resources.zh-TW.xaml", UriKind.Relative) };
                    break;
            }
        }
    }
}
