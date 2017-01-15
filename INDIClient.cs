﻿/* This file is part of INDISharp, Copyright © 2014-2015 Ilia Platone <info@iliaplatone.com>.
*
*  INDISharp is free software: you can redistribute it and/or modify
*  it under the terms of the GNU General Public License as published by
*  the Free Software Foundation, either version 3 of the License, or
*  (at your option) any later version.
*  
*  INDISharp is distributed in the hope that it will be useful,
*  but WITHOUT ANY WARRANTY; without even the implied warranty of
*  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
*  GNU General Public License for more details.
*  
*  You should have received a copy of the GNU General Public License
*  along with INDISharp.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.Xml.Linq;
using System.Text;
using System.IO;
using System.Collections;
using System.Globalization;
using System.Windows.Forms;
using System.Xml;

namespace INDI
{
    #region Event Argument classes
    public class MessageReceivedEventArgs : EventArgs
    {
        public string Message;
        public MessageReceivedEventArgs(string msg)
        {
            Message = msg;
        }
    }

    public class DeviceAddedEventArgs : EventArgs
    {
        public INDIDevice Device;
        public DeviceAddedEventArgs(INDIDevice dev)
        {
            Device = dev;
        }
    }

    public class MessageSentEventArgs : EventArgs
    {
        public string Message;
        public MessageSentEventArgs(string msg)
        {
            Message = msg;
        }
    }

    public class IsNewTextEventArgs : EventArgs
    {
        public ITextVector Vector;
        public string Device;
        public IsNewTextEventArgs(ITextVector vector, string dev)
        {
            Vector = vector;
            Device = dev;
        }
    }

    public class IsNewNumberEventArgs : EventArgs
    {
        public INumberVector Vector;
        public string Device;
        public IsNewNumberEventArgs(INumberVector vector, string dev)
        {
            Vector = vector;
            Device = dev;
        }
    }

    public class IsNewSwitchEventArgs : EventArgs
    {
        public ISwitchVector Vector;
        public string Device;
        public IsNewSwitchEventArgs(ISwitchVector vector, string dev)
        {
            Vector = vector;
            Device = dev;
        }
    }

    public class IsNewBlobEventArgs : EventArgs
    {
        public IBlobVector Vector;
        public string Device;
        public IsNewBlobEventArgs(IBlobVector vector, string dev)
        {
            Vector = vector;
            Device = dev;
        }
    }

    public class IsDelPropertyEventArgs : EventArgs
    {
        public string Vector;
        public string Device;
        public IsDelPropertyEventArgs(string vector, string dev)
        {
            Vector = vector;
            Device = dev;
        }
    }
    #endregion
    public class INDIClient : IDisposable
    {
        #region Private Variables
        TcpClient client;
        Stream stream;
        Thread ReadThread;
        Thread SendThread;
        Boolean ThreadsRunning;
        String _inputString = String.Empty;
        String _outputString = String.Empty;
        Queue inputString = new Queue();
        Queue outputString = new Queue();
        Int32 _BufferSize = 0x1000000;
        int _CommandSize = 0x10;
        #endregion
        #region Public Properties
        public Int32 BufferSize
        {
            get
            {
                return _BufferSize;
            }
            set
            {
                _BufferSize = value;
            }
        }
        public Int32 CommandSize
        {
            get
            {
                return _CommandSize;
            }
            set
            {
                _CommandSize = value;
            }
        }
        public Boolean Connected
        {
            get
            {
                if (client != null)
                    return client.Connected;
                return false;
            }
        }
        public String InputString
        {
            get
            {
                return _inputString;
            }
            set
            {
                _inputString = value.Replace("\r", "").Replace("\n", "");
                byte[] tosend = Encoding.UTF8.GetBytes(_inputString);
                MemoryStream ms = new MemoryStream(tosend);

                ReadThread = new Thread(new ParameterizedThreadStart(_readThread));
                ReadThread.IsBackground = true;
                ReadThread.Name = "INDISharp Client read thread";
                ReadThread.Start(ms);
            }
        }

        public String OutputString
        {
            get
            {
                if (outputString.Count > 0)
                    return (string)outputString.Dequeue();
                return String.Empty;
            }
            set
            {
                _outputString += value.Replace("\r", "").Replace("\n", "");
                outputString.Enqueue(_outputString);
                _outputString = String.Empty;
            }
        }
        #endregion
        #region Public Variables
        public string Name = "";
        public string Address = "127.0.0.1";
        public int Port = 7624;
        public List<INDIDevice> Devices = new List<INDIDevice>();
        public event EventHandler<MessageSentEventArgs> MessageSent;
        public event EventHandler<DeviceAddedEventArgs> DeviceAdded;
        public event EventHandler<IsDelPropertyEventArgs> IsDelProperty;
        public event EventHandler<IsNewBlobEventArgs> IsNewBlob;
        public event EventHandler<IsNewTextEventArgs> IsNewText;
        public event EventHandler<IsNewNumberEventArgs> IsNewNumber;
        public event EventHandler<IsNewSwitchEventArgs> IsNewSwitch;
        #endregion
        #region Constructors / Initialization

        public INDIClient(string uri)
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            Uri u = new Uri("http://" + uri);
            Port = u.Port;
            if (!uri.Contains(":"))
                Port = 7624;
            Address = u.Host;
        }

        public INDIClient(string address, int port)
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            Address = address;
            Port = port;
        }

        public INDIClient(Stream s)
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            Address = String.Empty;
            Port = 0;
            stream = s;
        }

        public INDIClient()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            Address = String.Empty;
            Port = 0;
            stream = null;
        }

        public bool Connect(string address = "", int port = 0)
        {
            if (address != "")
                Address = address;
            if (port != 0)
                Port = port;
            ThreadsRunning = true;
            if (Address != "" && Port > 0)
            {
                try
                {
                    client = new TcpClient(Address, Port);
                    if (Connected)
                    {
                        stream = client.GetStream();
                        SendThread = new Thread(_sendThread);
                        SendThread.IsBackground = true;
                        SendThread.Name = "INDISharp Client send thread";
                        SendThread.Start();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "ERROR");
                }
            }
			if (stream != null)
            {
                ReadThread = new Thread(new ParameterizedThreadStart(_readThread));
                ReadThread.IsBackground = true;
                ReadThread.Name = "INDISharp Client read thread";
                ReadThread.Start(null);
                return true;
			}
			return false;
        }

        public void Dispose()
        {
            Disconnect();
            Devices.Clear();
        }

        public void Disconnect()
        {
            ThreadsRunning = false;
            Thread.Sleep(100);
            outputString.Clear();
            inputString.Clear();
            if (stream != null)
                stream.Close();
            if (client != null)
                client.Close();
        }
        #endregion
        #region Threads
        void _sendThread()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            while (ThreadsRunning)
            {
                try
                {
                    if (outputString.Count > 0 && Connected)
                    {
                        string message = OutputString;
                        if (!String.IsNullOrEmpty(message))
                        {
                            byte[] buffer = Encoding.UTF8.GetBytes(message);
                            stream.Write(buffer, 0, buffer.Length);
                            MessageSent?.Invoke(this, new MessageSentEventArgs(message));
                        }
                    }
                }
                catch
                {
                    continue;
                }
                Thread.Sleep(100);
            }
        }

	    void _readThread(object s = null)
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            string action = "";
            string target = "";
            string device = "";
            string name = "";
            string label = "";
            string vectorname = "";
            string vectorlabel = "";
            string group = "";
            string permission = "";
            string rule = "";
            string format = "";
            string length = "";
            string minimum = "";
            string maximum = "";
            string step = "";
            IBlobVector blobvector = new IBlobVector("", "", "", "", "", "", null);
            List<INDIBlob> blobs = new List<INDIBlob>();
            ISwitchVector switchvector = new ISwitchVector("", "", "", "", "", "", null);
            List<INDISwitch> switches = new List<INDISwitch>();
            INumberVector numbervector = new INumberVector("", "", "", "", "", "", null);
            List<INDINumber> numbers = new List<INDINumber>();
            ITextVector textvector = new ITextVector("", "", "", "", "", "", null);
            List<INDIText> texts = new List<INDIText>();
            try
            {
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.ConformanceLevel = ConformanceLevel.Fragment;
                
                XmlReader reader = XmlReader.Create(s != null ? (Stream)s : stream, settings);
                while (reader.Read() && ThreadsRunning)
                {
                    try
                    {
                        switch (reader.NodeType)
                        {
                            case XmlNodeType.Element:
                                action = reader.Name.ToLower().Substring(0, 3);
                                target = reader.Name.Substring(3, reader.Name.Length - 3).ToLower();
                                action = action == null ? "" : action;
                                target = target == null ? "" : target;
                                if (action == "del" && target.Contains("property"))
                                {
                                    try
                                    {
                                        if (IsDelProperty != null && name != String.Empty)
                                            IsDelProperty(this, new IsDelPropertyEventArgs(name, reader.GetAttribute("device")));
                                    }
                                    catch { }
                                }
                                if (action == "get" && target.Contains("properties"))
                                {
                                    try
                                    {
                                        DefineProperties(reader.GetAttribute("device"));
                                    }
                                    catch { }
                                }
                                if (target.Contains("vector"))
                                {
                                    device = reader.GetAttribute("device");
                                    vectorname = reader.GetAttribute("name");
                                    vectorlabel = reader.GetAttribute("label");
                                    group = reader.GetAttribute("group");
                                    permission = reader.GetAttribute("perm");
                                    rule = reader.GetAttribute("rule");
                                    device = device == null ? "" : device;
                                    vectorname = vectorname == null ? "" : vectorname;
                                    vectorlabel = vectorlabel == null ? "" : vectorlabel;
                                    group = group == null ? "" : group;
                                    permission = permission == null ? "ro" : permission;
                                    rule = rule == null ? "" : rule;
                                    if (device == String.Empty || vectorname == String.Empty)
                                        break;
                                    AddDevice(new INDIDevice(device, this));
                                    if (target.Contains("blob"))
                                    {
                                        blobs = new List<INDIBlob>();
                                        blobvector = GetDevice(device).GetBlobVector(vectorname);
                                        if (blobvector == null)
                                            blobvector = new IBlobVector(device, vectorname, vectorlabel, group, permission, rule, blobs);
                                        blobvector.Values = blobs;
                                    }
                                    if (target.Contains("switch"))
                                    {
                                        switches = new List<INDISwitch>();
                                        switchvector = GetDevice(device).GetSwitchVector(vectorname);
                                        if (switchvector == null)
                                            switchvector = new ISwitchVector(device, vectorname, vectorlabel, group, permission, rule, switches);
                                        switchvector.Values = switches;
                                    }
                                    if (target.Contains("number"))
                                    {
                                        numbers = new List<INDINumber>();
                                        numbervector = GetDevice(device).GetNumberVector(vectorname);
                                        if (numbervector == null)
                                            numbervector = new INumberVector(device, vectorname, vectorlabel, group, permission, rule, numbers);
                                        numbervector.Values = numbers;
                                    }
                                    if (target.Contains("text"))
                                    {
                                        texts = new List<INDIText>();
                                        textvector = GetDevice(device).GetTextVector(vectorname);
                                        if (textvector == null)
                                            textvector = new ITextVector(device, vectorname, vectorlabel, group, permission, rule, texts);
                                        textvector.Values = texts;
                                    }
                                }
                                else
                                {
                                    name = reader.GetAttribute("name");
                                    label = reader.GetAttribute("label");
                                    name = name == null ? "" : name;
                                    label = label == null ? "" : label;
                                    if (target.Contains("blob"))
                                    {
                                        format = reader.GetAttribute("format");
                                        length = reader.GetAttribute("size");
                                        format = format == null ? "" : format;
                                        length = length == null ? "1" : length;
                                    }
                                    if (target.Contains("number"))
                                    {
                                        format = reader.GetAttribute("format");
                                        minimum = reader.GetAttribute("minimum");
                                        maximum = reader.GetAttribute("maximum");
                                        step = reader.GetAttribute("step");
                                        format = format == null ? "" : format;
                                        minimum = minimum == null ? "1" : minimum;
                                        maximum = maximum == null ? "1" : maximum;
                                        step = step == null ? "1" : step;
                                    }
                                }
                                break;
                            case XmlNodeType.Text:
                                if (!target.Contains("vector"))
                                {
                                    if (target.Contains("blob"))
                                    {
                                        blobs.Add(new INDIBlob(name, label, format, Convert.FromBase64String(reader.Value.Replace("\n", "")), Int32.Parse(length)));
                                    }
                                    if (target.Contains("switch"))
                                    {
                                        switches.Add(new INDISwitch(name, label, reader.Value.Replace("\n", "").Contains("On")));
                                    }
                                    if (target.Contains("number"))
                                    {
                                        numbers.Add(new INDINumber(name, label, format, Double.Parse(minimum), Double.Parse(maximum), Double.Parse(step), Double.Parse(reader.Value.Replace("\n", ""))));
                                    }
                                    if (target.Contains("text"))
                                    {
                                        texts.Add(new INDIText(name, label, reader.Value.Replace("\n", "")));
                                    }
                                }
                                break;
                            case XmlNodeType.XmlDeclaration:
                                Console.WriteLine("<?xml version='1.0'?>");
                                break;
                            case XmlNodeType.EntityReference:
                                Console.WriteLine(reader.Name);
                                break;
                            case XmlNodeType.EndElement:
                                if (reader.Name.ToLower().Contains("vector"))
                                {
                                    if (device == String.Empty || vectorname == String.Empty)
                                        break;
                                    if (target.Contains("blob"))
                                    {
                                        if (IsNewBlob != null)
                                            IsNewBlob(this, new IsNewBlobEventArgs(blobvector, device));
                                    }
                                    if (target.Contains("switch"))
                                    {
                                        if (IsNewSwitch != null)
                                            IsNewSwitch(this, new IsNewSwitchEventArgs(switchvector, device));
                                    }
                                    if (target.Contains("number"))
                                    {
                                        if (IsNewNumber != null)
                                            IsNewNumber(this, new IsNewNumberEventArgs(numbervector, device));
                                    }
                                    if (target.Contains("text"))
                                    {
                                        if (IsNewText != null)
                                            IsNewText(this, new IsNewTextEventArgs(textvector, device));
                                    }
                                }
                                break;
                        }
                    }
                    catch (XmlException ex)
                    {
                        Console.WriteLine(ex.Message + Environment.NewLine + ex.StackTrace);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message + Environment.NewLine + ex.StackTrace);
                        continue;
                    }
                }
                reader.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
        #endregion
        #region Device releated methods
        public void AddDevice(INDIDevice dev)
        {
            if (GetDevice(dev.Name) == null && dev != null && dev.Name != String.Empty)
            {
                Devices.Add(dev);
                if (DeviceAdded != null)
                    DeviceAdded(this, new DeviceAddedEventArgs(GetDevice(dev.Name)));
            }
        }

        public void RemoveDevice(string dev)
        {
            if (GetDevice(dev) != null)
                Devices.Remove(GetDevice(dev));
        }

        public INDIDevice GetDevice(string name)
        {
            foreach (INDIDevice d in Devices)
                if (d.Name == name)
                    return d;
            return null;
        }

        public string QueryProperties()
        {
            string ret =
                new XElement("getProperties",
                    new XAttribute("version", "1.7")).ToString();
            OutputString = ret;
            return ret;
        }

        public string DefineProperties(string device = "")
        {
            string ret = String.Empty;
            if (device == String.Empty || device == null)
            {
                foreach (INDIDevice d in Devices)
                    ret += d.DefineProperties();
            }
            else
                ret = GetDevice(device).DefineProperties();
            OutputString = ret;
            return ret;
        }
        #endregion
        #region Stream releated methods, misc

        public Stream GetStream()
        {
            return stream;
        }
        #endregion
    }
}