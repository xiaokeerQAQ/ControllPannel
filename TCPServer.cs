using BLL.Hardware.ScanGang;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;


namespace WinFormsApp1321
{
    public class TCPServer
    {
        private readonly Socket _serverSocket;
        private readonly ConcurrentDictionary<Socket, string> _clients = new();
        private readonly ConcurrentDictionary<Socket, string> _clientIdentifiers = new();
        private readonly SelectionForm _selectionForm;
        private const int BufferSize = 1024;
        private volatile bool _isRunning;
        private readonly ScanGangBasic _scanGangBasic;
        private readonly PLCClient _plcClient;
        private readonly Form1 _form1;
        private Dictionary<string, Func<byte[], byte[]>> _responseHandlers;
        private readonly Dictionary<string, Func<byte[], byte[]>> _responseHandlersTest;
        private readonly Dictionary<string, Func<byte[], byte[]>> _responseHandlersFormal;
        private readonly object _lock = new();
        private string result;
        private string errStr;
        private byte[] _codeBytes;
        private byte[] _toleranceBytes;
        private byte[] _countBytes;
        private byte[] _defectPositionsBytes;
        // 这里使用静态变量来存储模式
        public static bool Mode { get; set; }    //1代表测试模式，0代表正式模式
        public static bool _canSendMessage { get; set; }
        public static byte[] aaData;
        public static byte[] bbData;
        private volatile bool _testCompleted = false;
        private volatile bool _formalCompleted = false;
        // 共享计数器（静态变量）
        private static int _scanAASuccessCount;
        private static int _scanBBSuccessCount;
        // 提供外部访问的只读属性
        public static int ScanAASuccessCount => _scanAASuccessCount;
        public static int ScanBBSuccessCount => _scanBBSuccessCount;



        private bool isAAReceived = false;
        private bool isBBReceived = false;
        
        
        public event Action<string> OnClientConnected;
        public event Action<string, string> OnMessageReceived;
        public event Action<string> OnClientDisconnected;
        public event Action<string> OnError;



        public TCPServer(PLCClient plcClient, ScanGangBasic scanGangBasic)
        {
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _plcClient = plcClient;
            _scanGangBasic = scanGangBasic;

            _responseHandlersFormal = new Dictionary<string, Func<byte[], byte[]>>
            {
                { "FE-55-AA-01-00-E0", HandleHeartbeatAA },
                { "FE-55-BB-01-00-E0", HandleHeartbeatBB },
                { "FE-55-AA-01-00-E4", HandleScanAASuccess },
                { "FE-55-BB-01-00-E4", HandleScanBBSuccess }

            };

            _responseHandlersTest = new Dictionary<string, Func<byte[], byte[]>>
            {
                { "FE-55-AA-01-00-E0",HandleHeartbeatAATest},
                { "FE-55-BB-01-00-E0",HandleHeartbeatBBTest},
                { "FE-55-AA-01-00-E3",HandleScanAASuccessTest},
                { "FE-55-BB-01-00-E3",HandleScanBBSuccessTest}
            };
        }

        public async Task StartWoLiuAsync()
        {
            try
            {
                _serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 6065));
                _serverSocket.Listen(10);
                _isRunning = true;
                Console.WriteLine("服务器已启动，等待客户端连接...");

                while (_isRunning)
                {
                    var client = await AcceptClientAsync();
                    if (client != null)
                    {
                        _ = HandleClientAsync(client);
                    }
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"服务器启动失败: {ex.Message}");
            }
            finally
            {
                Stop();
            }
        }

        private async Task<Socket> AcceptClientAsync()
        {
            try
            {
                var clientSocket = await _serverSocket.AcceptAsync();
                clientSocket.ReceiveTimeout = 5000;

                lock (_lock)
                {
                    _clients.TryAdd(clientSocket, null);
                }

                var clientEP = clientSocket.RemoteEndPoint?.ToString() ?? "未知客户端";
                OnClientConnected?.Invoke(clientEP);

                // 接收初始识别数据
                var initialData = await ReceiveMessageAsync(clientSocket);
                if (initialData == null || initialData.Length < 3)
                {
                    DisconnectClient(clientSocket);
                    return null;
                }

                var clientId = IdentifyClient(initialData);
                _clientIdentifiers[clientSocket] = clientId;

                // **第一条消息回复心跳**
                var heartbeatResponse = clientId switch
                {
                    "AA" => GenerateDefaultResponseAA(),
                    "BB" => GenerateDefaultResponseBB(),
                    _ => new byte[] { 0xFF, 0xFF } // 默认心跳
                };

                await SendMessageAsync(clientSocket, heartbeatResponse);
                return clientSocket;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"接受客户端失败: {ex.Message}");
                return null;
            }
        }

        private string IdentifyClient(byte[] initialData)
        {
            if (initialData.Length < 3) return "Unknown";
            return BitConverter.ToString(initialData, 0, 3) switch
            {
                "FE-55-AA" => "AA",
                "FE-55-BB" => "BB",
                _ => "Unknown"
            };
        }


        private async Task HandleClientAsync(Socket client)
        {
            try
            {
                while (true)
                {
                    var receivedData = await ReceiveMessageAsync(client);
                    if (receivedData == null || receivedData.Length == 0) break;

                    // 根据模式检查结果
                    if (Mode == true)
                    {
                        _testCompleted = CheckTestResult(receivedData);
                    }
                    else
                    {
                        _formalCompleted = CheckFormalResult(receivedData);
                    }

                    var clientId = _clientIdentifiers.GetValueOrDefault(client, "Unknown");
                    var response = GenerateResponse(receivedData, clientId);
                    await SendMessageAsync(client, response);

                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"客户端处理错误: {ex.Message}");
            }
            finally
            {
                DisconnectClient(client);
            }
        }


        private byte[] GenerateResponse(byte[] receivedData, string clientId)
        {
            if (receivedData.Length < 6) return new byte[] { 0xFF, 0xFF };

            var key = BitConverter.ToString(receivedData, 0, 6);

            if (Mode == true)
            {
                _responseHandlers = _responseHandlersTest;
            }
            else
            {
                _responseHandlers = _responseHandlersFormal;
            }

            if (_responseHandlers.TryGetValue(key, out var handler))
            {
                return handler(receivedData);
            }

            return clientId switch
            {
                "AA" => GenerateDefaultResponseAA(),
                "BB" => GenerateDefaultResponseBB(),
                _ => new byte[] { 0xFF, 0xFF }
            };
        }

        private async Task<byte[]> ReceiveMessageAsync(Socket client)
        {
            try
            {
                var buffer = new byte[BufferSize];
                var received = await client.ReceiveAsync(buffer, SocketFlags.None);
                if (received == 0) return null;

                var data = new byte[received];
                Buffer.BlockCopy(buffer, 0, data, 0, received);
                OnMessageReceived?.Invoke(client.RemoteEndPoint.ToString(), BitConverter.ToString(data));
                return data;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"接收错误: {ex.Message}");
                return null;
            }
        }

        private async Task SendMessageAsync(Socket client, byte[] data)
        {
            try
            {
                if (client.Connected)
                {
                    await client.SendAsync(data, SocketFlags.None);
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"发送失败: {ex.Message}");
                DisconnectClient(client);
            }
        }

        private void DisconnectClient(Socket client)
        {
            if (client == null) return;

            lock (_lock)
            {
                if (!client.Connected) return;

                try
                {
                    var clientEP = client.RemoteEndPoint?.ToString() ?? "未知客户端";
                    client.Shutdown(SocketShutdown.Both);
                    client.Close();
                    OnClientDisconnected?.Invoke(clientEP);
                }
                finally
                {
                    _clients.TryRemove(client, out _);
                    _clientIdentifiers.TryRemove(client, out _);
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            lock (_lock)
            {
                foreach (var client in _clients.Keys)
                {
                    DisconnectClient(client);
                }
            }
            _serverSocket.Close();
            Console.WriteLine("服务器已关闭");
        }

        private byte CalculateCheckSum(byte[] data)
        {
            if (data.Length < 6) throw new ArgumentException("无效数据包");

            byte checkSum = 0;
            for (int i = 5; i < data.Length; i++)
            {
                checkSum ^= data[i];
            }
            return checkSum;
        }

        // 线程安全地增加计数
        public static void IncrementScanAASuccessCount()
        {
            Interlocked.Increment(ref _scanAASuccessCount);
        }

        public static void IncrementScanBBSuccessCount()
        {
            Interlocked.Increment(ref _scanBBSuccessCount);
        }

        public bool CheckTestResult(byte[] data)
        {
            // 1. 查找 E5 标志位
            int indexE5 = Array.IndexOf(data, (byte)0xE5);
            if (indexE5 == -1) return false;

            byte identifier = data[2]; // 记录 AA 或 BB 标志
            byte[] extractedData = data.Skip(indexE5 + 1).Take(data.Length - indexE5 - 1).ToArray();

            if (identifier == 0xAA && !isAAReceived)
            {
                isAAReceived = true;
                aaData = extractedData;
                Console.WriteLine("接收到AA检测");
            }
            else if (identifier == 0xBB && !isBBReceived)
            {
                isBBReceived = true;
                bbData = extractedData;
                Console.WriteLine("接收到BB检测");
            }
            return isAAReceived && isBBReceived;    //需要前端在新一轮测试开始时重置为false

        }

        private bool CheckFormalResult(byte[] data)
        {
            // 1. 查找 E5 标志位
            int indexE6 = Array.IndexOf(data, (byte)0xE6);
            if (indexE6 == -1) return false;

            byte identifier = data[2]; // 记录 AA 或 BB 标志
            byte[] extractedData = data.Skip(indexE6 + 1).Take(data.Length - indexE6 - 1).ToArray();

            if (identifier == 0xAA && !isAAReceived)
            {
                isAAReceived = true;
                aaData = extractedData;
                Console.WriteLine("接收到AA检测");
            }
            else if (identifier == 0xBB && !isBBReceived)
            {
                isBBReceived = true;
                bbData = extractedData;
                Console.WriteLine("接收到BB检测");
            }
            return isAAReceived && isBBReceived;

        }

        //提供前端查询 _testCompleted 的方法
        public bool IsTestCompleted()
        {
            return _testCompleted;
        }

        //提供前端查询 _formalCompleted 的方法
        public bool IsFormalCompleted()
        {
            return _formalCompleted;
        }

        //重置每次循环的接受信息
        public void ResetTestStatus()
        {
            isAAReceived = false;
            isBBReceived = false;
            _scanAASuccessCount = 0;
            _scanBBSuccessCount = 0;
            aaData = Array.Empty<byte>();  // 赋值为空数组
            bbData = Array.Empty<byte>();  // 赋值为空数组
        }


        public async Task<bool> ProcessFinalTestData(int checkInterval = 100)
        {

            Console.WriteLine("开始等待测试数据...");

            // **等待数据就绪**
            while ((aaData == null || bbData == null))
            {
                await Task.Delay(checkInterval);
            }

            // **数据到达后，检查是否第一位是 0xA0**
            if (aaData[0] == 0xA1 || bbData[0] == 0xA1)
            {
                Console.WriteLine("检测到数据的第一位为 0xA0, 返回 false.");
                ResetTestStatus();

                return false; // 返回 false
            }

            Console.WriteLine("开始处理最终数据...");


            // **提取缺陷数量**
            int defectCountAA = BitConverter.ToInt32(aaData, 1);  // 读取 aaData 中缺陷数量 (4字节)
            int defectCountBB = BitConverter.ToInt32(bbData, 1);  // 读取 bbData 中缺陷数量 (4字节)

            // **提取缺陷检测数据**
            byte[] defectsAA = aaData.Skip(5).Take(defectCountAA).ToArray();
            byte[] defectsBB = bbData.Skip(5).Take(defectCountBB).ToArray();

            // **进行缺陷合并**
            byte[] combinedDefects = new byte[Math.Min(defectCountAA, defectCountBB)];
            for (int i = 0; i < combinedDefects.Length; i++)
            {
                combinedDefects[i] = (byte)((defectsAA[i] == 0xA0 || defectsBB[i] == 0xA0) ? 0xA0 : 0xA1);
            }

            // **判断是否所有缺陷都被检测到**
            bool allDefectsDetected = combinedDefects.All(defect => defect == 0xA0);

            if (allDefectsDetected)
            {
                Console.WriteLine("所有缺陷都已被检测出来。");
                ResetTestStatus();
                return true;
            }
            else
            {
                Console.WriteLine("某些缺陷未被检测出来。");
                ResetTestStatus();
                return false;
            }
        }

        //检测模式结果
        public async Task<bool> ProcessFinalFormalData(int checkInterval = 100)
        {
            Console.WriteLine("开始等待测试数据...");

            // **等待数据就绪**
            while ((aaData == null || bbData == null))
            {
                await Task.Delay(checkInterval);
            }

            // **数据到达后，检查第一位是否是 0xA0**
            bool isAA_OK = aaData[0] == 0xA0;
            bool isBB_OK = bbData[0] == 0xA0;

            if (isAA_OK && isBB_OK)
            {
                Console.WriteLine("检测通过：第一位均为 0xA0");
                ResetTestStatus();
                return true;
            }
            else
            {
                Console.WriteLine("检测失败：第一位不全是 0xA0");
                ResetTestStatus();
                return false;
            }
        }


        //默认心跳响应
        private byte[] GenerateDefaultResponseAA()
        {
            byte[] response = { 0xFD, 0x55, 0xAA, 0x01, 0x00, 0xF0 };
            byte checkSum = CalculateCheckSum(response);

            // 创建一个新数组，包含校验位
            return response.Concat(new byte[] { checkSum }).ToArray();
        }

        private byte[] GenerateDefaultResponseBB()
        {
            byte[] response = { 0xFD, 0x55, 0xBB, 0x01, 0x00, 0xF0 };
            byte checkSum = CalculateCheckSum(response);

            // 创建一个新数组，包含校验位
            return response.Concat(new byte[] { checkSum }).ToArray();
        }

        /*        //发送条码信息给客户端
                public async Task SendBarcodeInfoToClients(byte[] barcodeLength, byte[] barcodeBytes)
                {
                    foreach (var client in _clients.Keys.ToArray())
                    {
                        if (!client.Connected) continue;

                        var clientId = _clientIdentifiers.GetValueOrDefault(client, null);
                        if (clientId == null) continue;

                        // 调用 GenerateBarcodeResponse 来生成响应消息
                        var responseMessage = GenerateBarcodeResponse(clientId, barcodeLength, barcodeBytes);

                        // 发送消息到客户端
                        await SendMessageAsync(client, responseMessage);
                    }
                }*/

        /*        //生成条码响应
                private byte[] GenerateBarcodeResponse(string clientId, byte[] length, byte[] data)
                {
                    var prefix = clientId == "AA"
                        ? new byte[] { 0xFD, 0x55, 0xAA, 0x02, 0x00, 0xFB }
                        : new byte[] { 0xFD, 0x55, 0xBB, 0x02, 0x00, 0xFB };

                    List<byte> response = new List<byte>(prefix);
                    response.AddRange(length);
                    response.AddRange(data);
                    response.Add(CalculateCheckSum(response.ToArray()));
                    return response.ToArray();
                }*/

        //AA回复心跳时，携带样棒信息
        private byte[] HandleHeartbeatAATest(byte[] input)
        {
            // 计算条码长度，并转换为 4 字节数组（默认小端）
            int sampleLength = SelectionForm.CodeBytes.Length;

            if (sampleLength == 0)
            {
                MessageBox.Show("无样棒信息，请输入样棒信息后重试！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return new byte[] { 0xFD, 0x55, 0xAA, 0x01, 0x00, 0xF0 };
            }
            List<byte> response = new List<byte> { 0xFD, 0x55, 0xAA, 0x00, 0x00, 0xFA }; // 预留长度位 0x00, 0x00
            int startIndex = response.Count - 1;  // 记录 0xFA 后的数据起始位置

            // 追加数据
            byte[] lengthBytes = BitConverter.GetBytes(sampleLength);
            response.AddRange(lengthBytes);
            response.AddRange(SelectionForm.CodeBytes);
            response.AddRange(SelectionForm.ToleranceBytes);
            response.AddRange(SelectionForm.CountBytes);
            response.AddRange(SelectionForm.DefectPositionsBytes);
            DateTime now = DateTime.Now;
            long timestamp = now.Ticks;  // 获取 100 纳秒级时间戳（long类型 8字节）
            byte[] timestampBytes = BitConverter.GetBytes(timestamp); // 转换成小端存储的 8 字节数组
            response.AddRange(timestampBytes);

            // 计算数据段长度 (从 0xFA 之后到当前位置)
            int dataLength = response.Count - startIndex;

            // 填充动态长度（小端格式）
            response[3] = (byte)(dataLength & 0xFF);
            response[4] = (byte)((dataLength >> 8) & 0xFF);

            // 计算校验和
            response.Add(CalculateCheckSum(response.ToArray()));

            return response.ToArray();
        }

        //BB回复心跳时，携带样棒信息
        private byte[] HandleHeartbeatBBTest(byte[] input)
        {
            // 计算条码长度，并转换为 4 字节数组（默认小端）
            int sampleLength = SelectionForm.CodeBytes.Length;
            if (sampleLength == 0)
            {
                MessageBox.Show("无样棒信息，请输入样棒信息后重试！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return new byte[] { 0xFD, 0x55, 0xBB, 0x01, 0x00, 0xF0 };
            }
            List<byte> response = new List<byte> { 0xFD, 0x55, 0xBB, 0x00, 0x00, 0xFA }; // 预留长度位 0x00, 0x00
            int startIndex = response.Count - 1;  // 记录 0xFA 后的数据起始位置

            // 追加数据
            byte[] lengthBytes = BitConverter.GetBytes(sampleLength);
            response.AddRange(lengthBytes);
            response.AddRange(SelectionForm.CodeBytes);
            response.AddRange(SelectionForm.ToleranceBytes);
            response.AddRange(SelectionForm.CountBytes);
            response.AddRange(SelectionForm.DefectPositionsBytes);
            DateTime now = DateTime.Now;
            long timestamp = now.Ticks;  // 获取 100 纳秒级时间戳（long类型 8字节）
            byte[] timestampBytes = BitConverter.GetBytes(timestamp); // 转换成小端存储的 8 字节数组
            response.AddRange(timestampBytes);

            // 计算数据段长度 (从 0xFA 之后到当前位置)
            int dataLength = response.Count - startIndex;

            // 填充动态长度（小端格式）
            response[3] = (byte)(dataLength & 0xFF);
            response[4] = (byte)((dataLength >> 8) & 0xFF);

            // 计算校验和
            response.Add(CalculateCheckSum(response.ToArray()));

            return response.ToArray();
        }


        //AA确认样棒信息成功送达仪器（回复E4）
        private byte[] HandleScanAASuccessTest(byte[] input)
        {
            // 更新计数器
            IncrementScanAASuccessCount();
            /* // 检查是否 AA 和 BB 都已经收到
            CheckForNextStep();*/
            byte[] response = { 0xFD, 0x55, 0xAA, 0x01, 0x00, 0xF0 };
            byte checkSum = CalculateCheckSum(response);
            return response.Concat(new byte[] { checkSum }).ToArray();
        }
        //BB确认样棒信息成功送达仪器（回复E4）
        private byte[] HandleScanBBSuccessTest(byte[] input)
        {
            // 更新计数器
            IncrementScanBBSuccessCount();
            // 检查是否 AA 和 BB 都已经收到
            // CheckForNextStep();
            byte[] response = { 0xFD, 0x55, 0xBB, 0x01, 0x00, 0xF0 };
            byte checkSum = CalculateCheckSum(response);
            return response.Concat(new byte[] { checkSum }).ToArray();
        }

        // AA回复心跳时，携带试件信息
        private byte[] HandleHeartbeatAA(byte[] input)
        {
            // 2. 检查是否有条码信息
            byte[] barcodeLength = BitConverter.GetBytes(Form1.BarcodeBytes.Length);
            byte[] barcodeBytes = Form1.BarcodeBytes;
            //检查批次号信息
            byte[] batchLength = BitConverter.GetBytes(Form1.BatchNumber.Length);
            byte[] batchNumber = Form1.BatchNumber;
            // 3. 根据协议返回响应
            if (barcodeLength.Length > 0 && barcodeBytes.Length > 0 && batchLength.Length > 0 && batchNumber.Length > 0)
            {
                //组装返回数据
                List<byte> response = new List<byte> { 0xFD, 0x55, 0xAA, 0x00, 0x00, 0xFB };
                int startIndex = response.Count - 1;  // 记录 0xFB 的索引位置

                // 4. 追加数据
                response.AddRange(barcodeLength);  // 条码长度
                response.AddRange(barcodeBytes);   // 条码数据
                response.AddRange(batchLength);    // 批次号长度
                response.AddRange(batchNumber);    // 批次号数据
                DateTime now = DateTime.Now;
                long timestamp = now.Ticks;  // 获取 100 纳秒级时间戳（long类型 8字节）
                byte[] timestampBytes = BitConverter.GetBytes(timestamp); // 转换成小端存储的 8 字节数组
                response.AddRange(timestampBytes);

                // 5. 计算数据段长度 (包含 0xFB)
                int dataLength = response.Count - startIndex;

                // 6. 填充长度字段（小端格式）
                response[3] = (byte)(dataLength & 0xFF);
                response[4] = (byte)((dataLength >> 8) & 0xFF);

                // 7. 计算校验和
                response.Add(CalculateCheckSum(response.ToArray()));

                return response.ToArray();
            }
            else
            {
                //返回默认心跳
                byte[] response = { 0xFD, 0x55, 0xAA, 0x01, 0x00, 0xF0 };
                byte checkSum = CalculateCheckSum(response);
                // 创建一个新数组，包含校验位
                return response.Concat(new byte[] { checkSum }).ToArray();
            }
        }
        // BB回复心跳时，携带试件信息
        private byte[] HandleHeartbeatBB(byte[] input)
        {
            // 2. 检查是否有条码信息
            byte[] barcodeLength = BitConverter.GetBytes(Form1.BarcodeBytes.Length);
            byte[] barcodeBytes = Form1.BarcodeBytes;

            //检查批次号信息
            byte[] batchLength = BitConverter.GetBytes(Form1.BatchNumber.Length);
            byte[] batchNumber = Form1.BatchNumber;

            // 3. 根据协议返回响应
            if (barcodeLength.Length > 0 && barcodeBytes.Length > 0)
            {
                //组装返回数据
                List<byte> response = new List<byte> { 0xFD, 0x55, 0xAA, 0x00, 0x00, 0xFB };
                int startIndex = response.Count - 1;  // 记录 0xFB 的索引位置

                // 4. 追加数据
                response.AddRange(barcodeLength);  // 条码长度
                response.AddRange(barcodeBytes);   // 条码数据
                response.AddRange(batchLength);    // 批次号长度
                response.AddRange(batchNumber);    // 批次号数据
                DateTime now = DateTime.Now;
                long timestamp = now.Ticks;  // 获取 100 纳秒级时间戳（long类型 8字节）
                byte[] timestampBytes = BitConverter.GetBytes(timestamp); // 转换成小端存储的 8 字节数组
                response.AddRange(timestampBytes);
                // 5. 计算数据段长度 (包含 0xFB)
                int dataLength = response.Count - startIndex;

                // 6. 填充长度字段（小端格式）
                response[3] = (byte)(dataLength & 0xFF);
                response[4] = (byte)((dataLength >> 8) & 0xFF);

                // 7. 计算校验和
                response.Add(CalculateCheckSum(response.ToArray()));

                return response.ToArray();
            }
            else
            {
                byte[] response = { 0xFD, 0x55, 0xBB, 0x01, 0x00, 0xF0 };
                //返回默认心跳
                byte checkSum = CalculateCheckSum(response);
                // 创建一个新数组，包含校验位
                return response.Concat(new byte[] { checkSum }).ToArray();
            }
        }
        //AA确认试件信息成功送达仪器（回复E4）
        private byte[] HandleScanAASuccess(byte[] input)
        {
            // 更新计数器
            IncrementScanAASuccessCount();
            /* // 检查是否 AA 和 BB 都已经收到
            CheckForNextStep();*/
            byte[] response = { 0xFD, 0x55, 0xAA, 0x01, 0x00, 0xF0 };
            byte checkSum = CalculateCheckSum(response);
            return response.Concat(new byte[] { checkSum }).ToArray();
        }
        //BB确认试件信息成功送达仪器（回复E4）
        private byte[] HandleScanBBSuccess(byte[] input)
        {
            // 更新计数器
            IncrementScanBBSuccessCount();
            // 检查是否 AA 和 BB 都已经收到
            // CheckForNextStep();
            byte[] response = { 0xFD, 0x55, 0xBB, 0x01, 0x00, 0xF0 };
            byte checkSum = CalculateCheckSum(response);
            return response.Concat(new byte[] { checkSum }).ToArray();
        }
    }
}