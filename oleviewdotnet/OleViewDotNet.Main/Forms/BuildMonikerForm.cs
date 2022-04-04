﻿//    This file is part of OleViewDotNet.
//    Copyright (C) James Forshaw 2014, 2016
//
//    OleViewDotNet is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    OleViewDotNet is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with OleViewDotNet.  If not, see <http://www.gnu.org/licenses/>.

using OleViewDotNet.Database;
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;

namespace OleViewDotNet.Forms
{
    public partial class BuildMonikerForm : Form
    {
        private static Guid CLSID_NewMoniker = new Guid("ecabafc6-7f19-11d2-978e-0000f8757e2a");
        public BuildMonikerForm(string last_moniker)
        {
            InitializeComponent();
            textBoxMoniker.Text = last_moniker;
        }

        private IMoniker ParseMoniker(IBindCtx bind_context, string moniker_string)
        {
            if (moniker_string == "new")
            {
                Guid IID_IUnknown = COMInterfaceEntry.IID_IUnknown;
                IntPtr unk;
                int hr = COMUtilities.CoCreateInstance(ref CLSID_NewMoniker, IntPtr.Zero, CLSCTX.INPROC_SERVER, ref IID_IUnknown, out unk);
                if (hr != 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                try
                {
                    return (IMoniker)Marshal.GetObjectForIUnknown(unk);
                }
                finally
                {
                    Marshal.Release(unk);
                }
            }
            else
            {
                if (moniker_string.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                    moniker_string.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                    moniker_string.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                {
                    IMoniker moniker;
                    int hr = COMUtilities.CreateURLMonikerEx(null, moniker_string, out moniker, CreateUrlMonikerFlags.Uniform);
                    if (hr != 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }
                    return moniker;
                }
                
                int eaten = 0;
                return COMUtilities.MkParseDisplayName(bind_context, moniker_string, out eaten);
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            try
            {
                IBindCtx bind_context = COMUtilities.CreateBindCtx(0);
                if (checkBoxParseComposite.Checked)
                {
                    foreach (string m in textBoxMoniker.Text.Split('!'))
                    {
                        IMoniker moniker = ParseMoniker(bind_context, m);
                        if (Moniker != null)
                        {
                            Moniker.ComposeWith(moniker, false, out moniker);
                        }
                        Moniker = moniker;
                    }
                }
                else
                {
                    Moniker = ParseMoniker(bind_context, textBoxMoniker.Text);
                }
                MonikerString = textBoxMoniker.Text;
                BindContext = bind_context;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                EntryPoint.ShowError(this, ex);
            }
        }

        public IBindCtx BindContext { get; private set; }
        public IMoniker Moniker { get; private set; }
        public string MonikerString { get; private set; }
    }
}
