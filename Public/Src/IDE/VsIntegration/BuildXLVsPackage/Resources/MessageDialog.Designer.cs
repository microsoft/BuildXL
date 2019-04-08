// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.VsPackage.Resources
{
    public partial class MessageDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.m_label1 = new System.Windows.Forms.Label();
            this.m_messageTextBox = new System.Windows.Forms.RichTextBox();
            this.m_label2 = new System.Windows.Forms.Label();
            this.m_exceptionTextBox = new System.Windows.Forms.RichTextBox();
            this.m_button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // label1
            //
            this.m_label1.AutoSize = true;
            this.m_label1.Location = new System.Drawing.Point(3, 9);
            this.m_label1.Name = "label1";
            this.m_label1.Size = new System.Drawing.Size(50, 13);
            this.m_label1.TabIndex = 0;
            this.m_label1.Text = "Message";
            //
            // m_messageTextBox
            //
            this.m_messageTextBox.Location = new System.Drawing.Point(6, 26);
            this.m_messageTextBox.Name = "m_messageTextBox";
            this.m_messageTextBox.ReadOnly = true;
            this.m_messageTextBox.Size = new System.Drawing.Size(478, 77);
            this.m_messageTextBox.TabIndex = 1;
            this.m_messageTextBox.Text = string.Empty;
            //
            // label2
            //
            this.m_label2.AutoSize = true;
            this.m_label2.Location = new System.Drawing.Point(6, 120);
            this.m_label2.Name = "label2";
            this.m_label2.Size = new System.Drawing.Size(85, 13);
            this.m_label2.TabIndex = 2;
            this.m_label2.Text = "Exception Trace";
            //
            // m_exceptionTextBox
            //
            this.m_exceptionTextBox.Location = new System.Drawing.Point(9, 137);
            this.m_exceptionTextBox.Name = "m_exceptionTextBox";
            this.m_exceptionTextBox.ReadOnly = true;
            this.m_exceptionTextBox.Size = new System.Drawing.Size(475, 96);
            this.m_exceptionTextBox.TabIndex = 3;
            this.m_exceptionTextBox.Text = string.Empty;
            this.m_exceptionTextBox.WordWrap = false;
            //
            // button1
            //
            this.m_button1.Location = new System.Drawing.Point(409, 239);
            this.m_button1.Name = "button1";
            this.m_button1.Size = new System.Drawing.Size(75, 23);
            this.m_button1.TabIndex = 4;
            this.m_button1.Text = "OK";
            this.m_button1.UseVisualStyleBackColor = true;
            this.m_button1.Click += new System.EventHandler(this.Button1_Click);
            //
            // MessageDialog
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(496, 268);
            this.Controls.Add(this.m_button1);
            this.Controls.Add(this.m_exceptionTextBox);
            this.Controls.Add(this.m_label2);
            this.Controls.Add(this.m_messageTextBox);
            this.Controls.Add(this.m_label1);
            this.Name = "MessageDialog";
            this.Text = "BuildXL Package";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label m_label1;
        private System.Windows.Forms.RichTextBox m_messageTextBox;
        private System.Windows.Forms.Label m_label2;
        private System.Windows.Forms.RichTextBox m_exceptionTextBox;
        private System.Windows.Forms.Button m_button1;
    }
}
