﻿﻿﻿using System;
using System.Windows.Forms;
using OfficeOpenXml;

namespace CringometrMDSPC
{
    internal static class Program
    {
        /// <summary>
        /// Główny punkt wejścia dla aplikacji.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Wymagane dla EPPlus 5+
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var boot = new BootOptionsForm())
            {
                if (boot.ShowDialog() == DialogResult.OK)
                {
                    // LocalizationManager.SetLanguage("pl-PL"); // Można ustawić język tutaj, jeśli BootOptionsForm miałby wybór języka
                    Application.Run(new Form1(boot.Options));
                }
            }
        }
    }
}
