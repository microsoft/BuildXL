// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Windows.Forms;

namespace BuildXL.VsPackage.Resources
{
    /// <summary>
    /// MessageDialog class to show messages to user
    /// </summary>
    [System.ComponentModel.DesignerCategory("")]
    public partial class MessageDialog : Form
    {
        /// <summary>
        /// Main constructor
        /// </summary>
        public MessageDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Constructor to initialize both message and trace
        /// </summary>
        /// <param name="message">The message that needs to be shown</param>
        /// <param name="trace">The stack trace</param>
        public MessageDialog(string message, string trace)
        {
            InitializeComponent();
            m_messageTextBox.Text = message;
            m_exceptionTextBox.Text = trace;
        }

        /// <summary>
        /// Constructor to initialize only message
        /// </summary>
        /// <param name="message">The message that needs to be shown</param>
        public MessageDialog(string message)
        {
            InitializeComponent();
            m_messageTextBox.Text = message;
        }

        /// <summary>
        /// Handler for button click
        /// </summary>
        /// <param name="sender">The sender</param>
        /// <param name="e">The event arguments</param>
        private void Button1_Click(object sender, EventArgs e)
        {
            Dispose();
        }
    }
}
