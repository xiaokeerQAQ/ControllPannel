using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;

namespace WinFormsApp1321
{
    public class PLCClient
    {
        private readonly string plcIp;
        private readonly int plcPort;
        private TcpClient client;
        private NetworkStream stream;
        private Timer heartbeatTimer; // 定时器发送心跳
        /*        private readonly byte[] heartbeatCommand = {0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x0E, 0x00, 0x00, 0x00, 0x01, 0x14, 0x00, 0x00, 0x60, 0x08, 0x00, 0xA8, 0x01, 0x00, 0x00, 0x00 }; // 心跳指令
        */
        public PLCClient(string ip, int port)
        {
            plcIp = ip;
            plcPort = port;
        }

        // 连接PLC (异步)
        public async Task<bool> ConnectAsync()
        {
            try
            {
                client = new TcpClient();
                await client.ConnectAsync(plcIp, plcPort);
                stream = client.GetStream();
                Console.WriteLine("✅ 连接PLC成功");
                /*                // 启动心跳定时器，每隔1秒发送一次
                                heartbeatTimer = new Timer(_ => Task.Run(async () => await SendHeartbeatAsync()), null, 10000, 10000);*/
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 连接PLC失败: {ex.Message}");

                // 连接失败时弹出窗口提示
                MessageBox.Show($"无法连接到PLC: {ex.Message}", "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return false;
            }
        }


        // 关闭连接
        public void Close()
        {
            stream?.Dispose();  // 直接调用 Dispose()
            client?.Close();
            Console.WriteLine("🔌 已断开PLC连接");
        }

        private int heartbeatRegister = 2144;
        // 发送心跳指令 (异步)
        public async Task SendHeartbeatAsync()
        {
            try
            {
                if (stream == null || !client.Connected)
                {
                    Console.WriteLine("⚠️ 连接断开，无法发送心跳");
                    return;
                }

                // 1. 读取心跳值
                int[] readValue = await ReadDRegisterAsync(heartbeatRegister, 1);
                if (readValue == null || readValue.Length == 0)
                {
                    Console.WriteLine("❌ 读取心跳值失败");
                    return;
                }

                int heartbeatValue = readValue[0]; // 获取 D2144 的当前值
                heartbeatValue = (heartbeatValue % 60) + 1; // 1~60 循环

                // 2. 写入心跳值
                bool success = await WriteDRegisterAsync(heartbeatRegister, heartbeatValue);
                if (success)
                {
                    Console.WriteLine($"💓 发送心跳成功，当前值: {heartbeatValue}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 心跳发送失败: {ex.Message}");
            }
        }

        // 发送SLMP指令并接收响应 (异步)
        private async Task<byte[]> SendAndReceiveAsync(byte[] command)
        {
            try
            {
                await stream.WriteAsync(command, 0, command.Length);

                byte[] response = new byte[512]; // 预留足够空间
                int bytesRead = await stream.ReadAsync(response, 0, response.Length);
                if (bytesRead == 0) // 🔹 检测是否读到了数据
                {
                    Console.WriteLine("⚠️ 读取到 0 字节数据，可能是 PLC 没有响应！");
                    return null; // **返回 null，而不是空数组**
                }
                Array.Resize(ref response, bytesRead); // 截取有效数据
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 通信错误: {ex.Message}");
                return null;
            }
        }

        public async Task<int[]> ReadDRegisterAsync(int address, int count)
        {
            byte[] command = BuildReadDCommand(address, count);
            byte[] response = await SendAndReceiveAsync(command);

            // 计算期望的响应长度：
            int expectedLength = 7 + 2 + 2 + (count * 2); // 7B (帧头) + 2B (数据长度) + 2B (结束代码) + 数据区(count * 2)
            if (response == null || response.Length < expectedLength) return null; // 确保响应长度足够

            // 提取数据区 (从索引 11 开始, 即 7 + 2 + 2)
            byte[] data = new byte[count * 2];
            Array.Copy(response, 11, data, 0, data.Length);

            // 解析 16 位整数 (每个寄存器 2 字节, 低字节在前)
            int[] values = new int[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = BitConverter.ToInt16(data, i * 2);
            }

            return values;
        }



        //    写入 D 寄存器 (异步) (例如: D100 = 5678)
        private SemaphoreSlim _plcWriteLock = new SemaphoreSlim(1, 1); // 1表示同时只有一个任务可以访问

        public async Task<bool> WriteDRegisterAsync(int address, int value)
        {
            await _plcWriteLock.WaitAsync(); // 确保只有一个任务能写入
            try
            {
                // 构造写入指令
                byte[] command = BuildWriteDCommand(address, value);

                // 发送指令并接收响应
                byte[] response = await SendAndReceiveAsync(command);

                // 如果响应为空或者响应长度不足，说明出错
                if (response == null || response.Length < 11)
                {
                    Console.WriteLine("❌ 响应无效，长度不足");
                    return false;
                }

                // 计算结束代码所在的位置
                int endCodeIndex = response.Length - 2;

                // 读取结束代码（最后两个字节，小端模式）
                int endCode = BitConverter.ToUInt16(response, endCodeIndex);

                // 如果结束代码为0x0000，表示写入成功
                if (endCode == 0x0000)
                {
                    Console.WriteLine($"✅ D寄存器 {address} 写入成功，值：{value}");
                    return true;
                }
                else
                {
                    // 如果结束代码不是0x0000，表示写入失败或发生异常
                    Console.WriteLine($"❌ D寄存器 {address} 写入失败，错误代码: {endCode}");

                    // 提取异常信息并打印
                    byte[] exceptionData = new byte[response.Length - 11];
                    Array.Copy(response, 11, exceptionData, 0, exceptionData.Length);
                    Console.WriteLine($"异常信息: {BitConverter.ToString(exceptionData)}");

                    return false;
                }
            }
            finally
            {
                _plcWriteLock.Release(); // 释放锁，让其他任务可以写入
            }
        }


        private byte[] BuildReadDCommand(int address, int count)
        {
            return new byte[]
            {
        0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,  // 头部
        0x0C, 0x00,  // 数据长度 12
        0x00, 0x00,  // 保留
        0x01, 0x04,  // 指令
        0x00, 0x00,  // 子命令
        (byte)(address & 0xFF),          // 低字节
        (byte)((address >> 8) & 0xFF),   // 中间字节
        (byte)((address >> 16) & 0xFF),  // 高字节
        0xA8,                            // D寄存器标识符 (0xA8)
        (byte)(count & 0xFF),            // 读取点数低字节
        (byte)((count >> 8) & 0xFF)      // 读取点数高字节
            };
        }


        // 生成写入 D 寄存器的 SLMP 指令
        private byte[] BuildWriteDCommand(int address, int value)
        {
            return new byte[]
            {
            0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,  // 头部  
            0x0E, 0x00,  // 数据长度14
            0x00, 0x00,  // 保留
            0x01, 0x14,  // 指令
            0x00, 0x00,  // 子命令
            (byte)(address & 0xFF),          // 低字节
            (byte)((address >> 8) & 0xFF),   // 中间字节
            (byte)((address >> 16) & 0xFF),  // 高字节
           // (byte)(address & 0xFF), (byte)((address >> 8) & 0xFF),  // 地址
            0xA8,  // D寄存器z标识符 (0xA8)
            0x01, 0x00,  //软元件点数
            // 将 value 按照 2 字节插入
           (byte)(value & 0xFF),  // 低字节
           (byte)((value >> 8) & 0xFF),  // 高字节
            };
        }
    }
}
