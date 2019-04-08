// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TypeScript.Converter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            string inputText = InputBox.Text;

            StringReader reader = new StringReader(RegexBox.Text ?? string.Empty);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split(new[] { "==>" }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    try
                    {
                        inputText = Regex.Replace(inputText, parts[0], parts[1].Replace("\\n", Environment.NewLine));
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }

            inputText = ChangeToCamelCase(inputText);

            OutputBox.Text = inputText;
        }

        private static string ChangeToCamelCase(string text)
        {
            return Regex.Replace(text, @"(\w+)\.(\w+)", (Match m) => m.Groups[1].Value + "." + ToCamelCase(m.Groups[2].Value));

        }

        private static string ToCamelCase(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text; }

            return new string(text[0], 1).ToUpperInvariant() + text.Substring(1);
        }
    }
}
