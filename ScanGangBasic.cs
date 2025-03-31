using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BLL.Hardware.ScanGang
{
    public delegate void ScanGangStatus(string name, bool connectStatus);

    public class ScanGangBasic
    {
        private string _name = "";

        private Socket _socketCore = null;

        public event ScanGangStatus ConnectedStatus = null;

        private bool _connectSuccess = false;

        private byte[] _buffer = new byte[2048];

        private string _resString = "";

        private object _lock = new object();

        private bool _hasResult = false;

        // 日志记录方法
        private static string logFilePath = "log.txt";  // 日志文件路径
     

        // 写入日志
        private static void WriteLog(string message)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(logFilePath, true))
                {
                    sw.WriteLine($"{DateTime.Now}: {message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"写入日志失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 打印到控制台 (可选)
        private static void PrintLog(string message)
        {
            Console.WriteLine($"{DateTime.Now}: {message}");
        }


        public bool Connect(string ip, int portNo, string name, out string err)
        {
            _name = name;
            err = string.Empty;
            try
            {
                Socket socket = this._socketCore;
                if (socket != null)
                {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
                this._socketCore = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                this._connectSuccess = false;
                Task.Run(new Action(() =>
                {
                    ///两秒超时判断防止占用卡死
                    Thread.Sleep(2000);
                    bool flag = !this._connectSuccess;
                    if (flag)
                    {
                        Socket socket2 = this._socketCore;
                        if (socket2 != null)
                        {
                            socket2.Close();
                        }
                    }
                }));
                this._socketCore.Connect(IPAddress.Parse(ip), portNo);
                this._connectSuccess = true;
                this._socketCore.BeginReceive(this._buffer, 0, 2048, SocketFlags.None, new AsyncCallback(this.ReceiveCallBack), this._socketCore);
                //连接成功 StringResources.Language.ConnectServerSuccess;
            }
            catch (Exception ex)
            {
                WriteLog($"扫码器连接失败: {ex.Message}");
                MessageBox.Show($"连接失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ConnectedStatus?.Invoke(_name, false);
            }
            return _connectSuccess;
        }

        public void DisConnect()
        {
            Socket socket = this._socketCore;
            if (socket != null && socket.Connected)
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }

        }

        /// <summary>
        /// 扫码
        /// </summary>
        /// <param name="result">返回扫码结果</param>
        /// <param name="errStr">返回错误消息</param>
        /// <returns></returns>
        public bool ScanOnce(out string result, out string errStr)
        {
            var comd = SendCommd("S", out errStr);
            int tick = Environment.TickCount;
            while (!_hasResult)
            {
                Thread.Sleep(10);
                if (Environment.TickCount - tick >= 1000) // 1秒超时
                {
                    errStr = "扫码超时";
                    result = string.Empty;
                    return false;
                }
            }
            lock (_lock)
            {
                result = _resString;
            }
            if (result == "NOREAD" || result == "READ")
            {
                result = "未扫描到条码";
            }
            if (!comd)
            {
                return false;
            }
            else
            {
                if (!string.IsNullOrEmpty(errStr) || string.IsNullOrEmpty(result))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 数据回调处理
        /// </summary>
        /// <param name="ar"></param>
        public void ReceiveCallBack(IAsyncResult ar)
        {
            try
            {
                if (!_socketCore.Connected) return;
                int length = this._socketCore.EndReceive(ar);
                this._socketCore.BeginReceive(this._buffer, 0, 2048, SocketFlags.None, new AsyncCallback(this.ReceiveCallBack), this._socketCore);
                bool flag = length == 0;
                if (!flag)
                {
                    byte[] data = new byte[length];
                    Array.Copy(this._buffer, 0, data, 0, length);
                    var msg = Encoding.ASCII.GetString(data);

                    if (msg != "\r\n")
                    {
                        lock (_lock)
                        {
                            _resString = string.Empty;

                            if (msg.Contains("\r\n"))
                            {
                                msg.Replace("\r\n", "");
                            }
                            _resString = msg.Trim();
                            _hasResult = true;
                        }
                    }
                    WriteLog($"接收到扫码器数据: {_resString}");
                }
            }
            catch (Exception ex)
            {
                _resString = string.Empty;
                WriteLog($"扫码器数据回调处理异常: {ex.Message}");
                MessageBox.Show($"数据回调处理异常: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 发送扫码命令
        /// </summary>
        /// <param name="commd">触发命令</param>
        /// <param name="errStr">错误消息</param>
        /// <returns></returns>
        public bool SendCommd(string commd, out string errStr)
        {
            errStr = string.Empty;
            var send = Encoding.ASCII.GetBytes(commd);
            try
            {
                Socket socket = this._socketCore;
                if (socket != null)
                {
                    socket.Send(send, 0, send.Length, SocketFlags.None);
                }
                _hasResult = false;
                return true;
            }
            catch (Exception ex)
            {
                errStr = ex.Message;
                WriteLog($"发送命令失败: {ex.Message}");
                MessageBox.Show($"发送命令失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // 模拟设置条码
        public void SetBarcode(string barcode)
        {
            lock (_lock)
            {
                _resString = barcode;
            }
        }

        // 获取条码（以 byte[] 类型返回）
        public byte[] GetBarcodeBytes()
        {
            lock (_lock)
            {
                return Encoding.ASCII.GetBytes(_resString);  // 将条码转换为字节数组
            }
        }

        // 获取条码长度
        public byte[] GetBarcodeLength()
        {
            lock (_lock)
            {
                int length = _resString.Length;  // 获取条码的字符长度
                return BitConverter.GetBytes(length);  // 返回条码长度的字节数组
            }
        }


    }
}
