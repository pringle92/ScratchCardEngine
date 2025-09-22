#region Usings
using Microsoft.Win32;
using System;
using System.Windows;
#endregion

namespace ScratchCardGenerator.Common.Services
{
    /// <summary>
    /// Manages the application's visual theme (Light/Dark) by detecting the
    /// Windows system theme and loading the appropriate resource dictionaries.
    /// </summary>
    public static class ThemeManager
    {
        #region Public Methods

        /// <summary>
        /// Applies the application theme based on the current Windows theme setting.
        /// It checks the registry for the 'AppsUseLightTheme' value and loads the
        /// corresponding XAML resource files for both colours and control styles.
        /// </summary>
        public static void ApplyTheme()
        {
            try
            {
                const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
                const string RegistryValueName = "AppsUseLightTheme";

                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
                {
                    var registryValue = key?.GetValue(RegistryValueName) ?? 1;

                    if (registryValue is int appsUseLightTheme && appsUseLightTheme == 0)
                    {
                        SetTheme("DarkTheme");
                    }
                    else
                    {
                        SetTheme("LightTheme");
                    }
                }
            }
            catch (Exception)
            {
                SetTheme("LightTheme");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Clears existing resources and applies a new theme by loading both a colour
        /// dictionary and a general styles dictionary.
        /// </summary>
        /// <param name="themeName">The name of the theme file to load (e.g., "LightTheme" or "DarkTheme").</param>
        private static void SetTheme(string themeName)
        {
            var colorThemeUri = new Uri($"Themes/{themeName}.xaml", UriKind.Relative);
            var stylesUri = new Uri("Themes/Styles.xaml", UriKind.Relative);

            ResourceDictionary colorThemeDictionary = new ResourceDictionary { Source = colorThemeUri };
            ResourceDictionary stylesDictionary = new ResourceDictionary { Source = stylesUri };

            Application.Current.Resources.MergedDictionaries.Clear();

            Application.Current.Resources.MergedDictionaries.Add(colorThemeDictionary);
            Application.Current.Resources.MergedDictionaries.Add(stylesDictionary);
        }

        #endregion
    }
}
