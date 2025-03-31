using BLL.Hardware.ScanGang;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace WinFormsApp1321
{
    public partial class Form1 : Form
    {
        private bool isOn = false; // 按钮状态
        private bool isOn1 = false;
        private int currentCycle = 0; // 当前循环次数
        private int totalCycles = 0; // 总循环次数
        private CancellationTokenSource cancellationTokenSource; // 控制循环停止
        private bool isCalibrationMode = false;

        private System.Windows.Forms.Timer detectionTimer;
        private System.Windows.Forms.Timer heartbeatTimer;
        private TCPServer _tcpServer;
        private PLCClient _plcClient;
        private ScanGangBasic _scanGangBasic;
        public static byte[] BarcodeBytes { get; set; } = Array.Empty<byte>();
        public static byte[] BatchNumber { get; set; } = Array.Empty<byte>();
        private bool isManualStop = false;//标志位，是否在流程中点了停止
        private bool isProcessing = false;  // 标志位，确保每次只有一个试件在检测中

        public Form1()
        {
            InitializeComponent();
            textBox1.ReadOnly = true;
            textBox1.Text = "";
            textBox2.Enabled = false;
            button4.Enabled = false;
            button6.Enabled = false;
            button8.Enabled = false;
            detectionTimer = new System.Windows.Forms.Timer();
            detectionTimer.Interval = 5000;
            detectionTimer.Tick += DetectionTimer_Tick;

            heartbeatTimer = new System.Windows.Forms.Timer();
            heartbeatTimer.Interval = 4000; // 4秒
            heartbeatTimer.Tick += async (s, e) => await _plcClient.SendHeartbeatAsync();


            // 初始化 PLC 和扫码枪
            _plcClient = new PLCClient("127.0.0.1", 6000);
            _scanGangBasic = new ScanGangBasic();

            // 初始化 TCPServer，并传入 PLC 和扫码枪实例
            _tcpServer = new TCPServer(_plcClient, _scanGangBasic);
        }

        private void UpdateLabel8(string text)
        {
            if (label8.InvokeRequired)
            {
                label8.Invoke(new Action(() =>
                {
                    label8.Text = text;
                    label8.Visible = !string.IsNullOrWhiteSpace(text); // 为空时隐藏
                }));
            }
            else
            {
                label8.Text = text;
                label8.Visible = !string.IsNullOrWhiteSpace(text); // 为空时隐藏
            }
        }
        private async void button5_Click(object sender, EventArgs e)
        {
            SelectionForm selectionForm = new SelectionForm();
            selectionForm.ShowDialog();
            if (selectionForm.DialogResult == DialogResult.OK)
            {
                bool writeSuccess = await _plcClient.WriteDRegisterAsync(2130, 2);
                if (writeSuccess)
                {
                    // MessageBox.Show("无法写入 PLC（D2130 = 3），启动自校准失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    TCPServer.Mode = true;
                    //校准按钮
                    isOn1 = true;
                    button5.Enabled = false;
                    button6.Enabled = true;
                    //检测按钮
                    button7.Enabled = false;
                    button8.Enabled = false;
                    label1.Text = "当前状态：自校准模式";
                    label6.Text = "自校准模式运行中";
                    DialogResult result = MessageBox.Show(
              //$"系统文件：C:\\system\\system.ini\n" +
              $"标样文件：{selectionForm.StandardFilePath}\n" +
              $"标定循环次数：{selectionForm.CalibrationCount}\n" +
              "放入样棒后点击确认？",
              "放入样棒",
              MessageBoxButtons.OKCancel,
              MessageBoxIcon.Question
          );
                    if (result == DialogResult.Cancel)
                    {
                        bool Success = await _plcClient.WriteDRegisterAsync(2142, 1);
                        if (Success)
                        {
                            isOn1 = false;
                            button5.Enabled = true;
                            button6.Enabled = false;
                            //检测按钮
                            button7.Enabled = false;
                            button8.Enabled = false;
                            label1.Text = "当前状态：待机状态";
                            label6.Text = "自校准模式";
                        }
                        // MessageBox.Show("自校准模式未开启。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    else
                    {

                        // **执行校准循环**
                        string selectedStandardFile = selectionForm.StandardFilePath;
                        totalCycles = selectionForm.CalibrationCount;
                        currentCycle = 0;
                        cancellationTokenSource = new CancellationTokenSource();
                        Task.Run(() => RunCalibrationLoop(selectedStandardFile, cancellationTokenSource.Token));

                    }
                }
                else
                {

                    UpdateLabel8("报警信息：启动自校准失败！");
                    return;
                }
                return;
            }
        }



        private async void button6_Click_1(object sender, EventArgs e)
        {

            isManualStop = true;  // 设置人为停止标志
           /* UpdateLabel8("");
            StopCalibration(false);*/
        }



        private async void button1_Click(object sender, EventArgs e)
        {
        }
        private async void button2_Click(object sender, EventArgs e)
        {
            /* if (!isOn) // 进入检测模式
             {
                 bool writeSuccess = await _plcClient.WriteDRegisterAsync(2130, 1);

                 if (writeSuccess)
                 {
                     isOn = true;
                     button2.Text = "退出检测模式";
                     label1.Text = "当前状态：检测模式";
                     button1.Enabled = false;

                     textBox2.Enabled = true;
                     button4.Enabled = true;

                     detectionTimer.Start();
                 }
                 else
                 {
                     MessageBox.Show("无法进入检测模式，PLC通信失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                 }
             }
             else // 退出检测模式
             {
                 bool writeSuccess = await _plcClient.WriteDRegisterAsync(2140, 1);

                 if (writeSuccess)
                 {
                     isOn = false;
                     detectionTimer.Stop();
                     button2.Text = "进入检测模式";
                     label1.Text = "当前状态：待机状态";
                     button1.Enabled = true;
                 }
                 else
                 {
                     MessageBox.Show("无法退出检测模式，PLC通信失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                 }
             }*/
        }


        private async void DetectionTimer_Tick(object sender, EventArgs e)
        {
            while (isOn)
            {
                int[] response = await _plcClient.ReadDRegisterAsync(2132, 1);

                if (response != null && response.Length > 0 && response[0] == 1)
                {
                    string result = string.Empty;
                    string errStr = string.Empty;

                    // **检查是否正在处理试件，避免重复扫码**
                    if (isProcessing)
                    {
                        // 如果正在处理试件，跳过当前循环
                        await Task.Delay(1000);  // 等待一段时间后再检查
                        continue;
                    }

                    // **等待扫码枪成功扫描**
                    while (true)
                    {
                        bool success = _scanGangBasic.ScanOnce(out result, out errStr);
                        if (success)
                        {
                            success = false;
                            break; // 成功扫描到条码，跳出循环
                        }
                        await Task.Delay(100); // 每 100ms 检查一次，避免 CPU 过载
                    }

                    // **更新条码数据**
                    Form1.BarcodeBytes = Encoding.UTF8.GetBytes(result);

                    // **等待 ScanAASuccessCount 和 ScanBBSuccessCount**
                    while (TCPServer.ScanAASuccessCount == 0 || TCPServer.ScanBBSuccessCount == 0)
                    {
                        await Task.Delay(100);
                    }

                    // **开始处理试件，设置标志位**
                    isProcessing = true;

                    // **发送条码通知 PLC**
                    bool writeSuccess = await _plcClient.WriteDRegisterAsync(2132, 3);
                    if (writeSuccess)
                    {
                        // **试件判断**
                        bool isTestPassed = await CheckTestResultWithoutTimeout();
                        int statusCode = isTestPassed ? 1 : 2;

                        await _plcClient.WriteDRegisterAsync(2138, statusCode);
                        Console.WriteLine($"试件检测结果：{(isTestPassed ? "合格" : "不合格")}，已发送至 D2138。");
                    }
                    else
                    {
                        Console.WriteLine("无法向 PLC 发送扫码成功信息！");
                    }

                    // **处理完成后重置标志位**
                    isProcessing = false;


                    // **检测停止信号**
                    int[] stopSignal = await _plcClient.ReadDRegisterAsync(2144, 2);
                    if (stopSignal[1] == 1)
                    {
                        Console.WriteLine("检测模式停止...");
                        //await StopDetectionAsync();
                        break; // 退出循环
                    }

                    await Task.Delay(1000);
                }
            }
        }



        private async Task StopDetectionAsync()
        {


            // 启用自校准按钮
            button5.Enabled = true;
            button6.Enabled = true;
            // 状态更新
            isOn = false;
            label9.Text = "检测模式";
            label1.Text = "当前状态：待机状态";
        }

        private void button3_Click(object sender, EventArgs e)
        {

        }

        public bool confirmWriteSuccess;
        private async Task RunCalibrationLoop(string selectedStandardFile, CancellationToken token)
        {
            DateTime lastCycleEndTime = DateTime.Now;
            string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system.ini");

            while (currentCycle < totalCycles && !token.IsCancellationRequested)
            {

                // **确保样棒准备就绪**
                bool sampleReady = await EnsureSampleReadyAsync();
                if (!sampleReady)
                {
                    StopCalibration(true);
                    return;
                }

                if (!await WaitForTestCompletion())
                {
                    StopCalibration(true);
                    return;
                }

                // 使用超时机制来判断测试结果是否合格
                bool isTestPassed = await CheckTestResultWithoutTimeout();
                int registerValue = isTestPassed ? 2 : 1;

                // 如果不合格，直接停止校准
                if (!isTestPassed)
                {
                    bool writeFail = await _plcClient.WriteDRegisterAsync(2142, registerValue);
                    MessageBox.Show("校准不合格，停止校准。", "不合格", MessageBoxButtons.OK);
                    StopCalibration(true);
                    return;
                }

                // 先检查是否人为停止
                if (isManualStop)  // 在 button6_Click_1 事件中修改它
                {
                    await _plcClient.WriteDRegisterAsync(2142, 1);  // 停止写入 1
                    UpdateLabel8("校准已人为停止");
                    StopCalibration(true);
                    return;  // 直接退出循环

                }

                // 否则，合格后继续写入 2
                bool writeSuccess = await _plcClient.WriteDRegisterAsync(2142, registerValue);
                UpdateLabel8("");
                UpdateCycleCount();

            }

            if (currentCycle == totalCycles)
            {
                CompleteCalibration(lastCycleEndTime, iniPath);
                //自校准次数已满通知PLC下料
                await _plcClient.WriteDRegisterAsync(2142, 1);
                //进入待机状态
                int[] response = await _plcClient.ReadDRegisterAsync(2144, 2);
                if (response == null)
                {
                    UpdateLabel8("报警信息：D2132无有效信息，检查连接");
                    return;
                }
                else if (response[1] == 1)
                {
                    label1.Text = "当前状态：自校准模式";
                    label6.Text = "自校准模式运行中";
                }
                return;
            }
        }

        private async Task<bool> EnsureSampleReadyAsync()
        {
            while (true)
            {
                int[] response = await _plcClient.ReadDRegisterAsync(2132, 1);

                if (response == null)
                {
                    UpdateLabel8("报警信息：无法读取D2132，检查连接");
                    return false;
                }

                if (response[0] == 1) // 扫码区有样棒
                {
                    // 等待扫码成功
                    while (TCPServer.ScanAASuccessCount == 0 || TCPServer.ScanBBSuccessCount == 0)
                    {
                        await Task.Delay(100);
                    }

                    // 写入 D2132 = 3，通知 PLC 开始循环
                    bool confirmWriteSuccess = await _plcClient.WriteDRegisterAsync(2132, 3);
                    if (!confirmWriteSuccess)
                    {
                        UpdateLabel8("报警信息：无法通知PLC开启循环");
                        return false;
                    }
                    return true;
                }

                // **如果 D2132 != 1，则持续弹窗**
                DialogResult retryResult = MessageBox.Show(
                    "请放入样棒后点击确认！",
                    "等待样棒",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning
                );

                if (retryResult == DialogResult.Cancel)
                {
                    UpdateLabel8("操作取消：未放入样棒");
                    return false;
                }

                await Task.Delay(100); // 每 100ms 重新检查扫码区状态
            }
        }


        private async Task<bool> CheckTestResultWithoutTimeout()
        {
            Task<bool> task;

            // 根据 TCPServer.Mode 选择调用不同的方法
            if (TCPServer.Mode)
            {
                task = Task.Run(() => _tcpServer.ProcessFinalTestData());
            }
            else
            {
                task = Task.Run(() => _tcpServer.ProcessFinalFormalData());
            }

            // 一直等待任务完成，不设置超时
            await task;

            UpdateLabel7("");
            return task.Result;
        }



        private async Task<bool> WaitForTestCompletion()
        {
            while (!_tcpServer.IsTestCompleted())
            {
                await Task.Delay(1000);
            }
            return true;
        }

        private void UpdateCycleCount()
        {
            currentCycle++;
            UpdateCycleLabel();
        }

        private void CompleteCalibration(DateTime lastCycleEndTime, string iniPath)
        {
            MessageBox.Show("所有循环已执行。", "完成", MessageBoxButtons.OK);

            DateTime validUntil = lastCycleEndTime.AddHours(8);
            WriteDeadlineToIni(iniPath, validUntil);
            UpdateValidUntilLabel(validUntil);

            this.Invoke(new Action(() => button7.Enabled = true));

            StopCalibration(false);
        }


      
        private void WriteDeadlineToIni(string iniPath, DateTime deadline)
        {
            try
            {
                List<string> lines = new List<string>();

                if (File.Exists(iniPath))
                {
                    lines = File.ReadAllLines(iniPath).ToList();
                }

                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("Deadline="))
                    {
                        lines[i] = $"Deadline={deadline:yyyy-MM-dd HH:mm:ss}"; // 直接更新
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    lines.Add($"Deadline={deadline:yyyy-MM-dd HH:mm:ss}"); // 确保一行
                }

                File.WriteAllLines(iniPath, lines);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"写入系统文件失败: {ex.Message}", "错误", MessageBoxButtons.OK);
            }
        }



        private void UpdateValidUntilLabel(DateTime validUntil)
        {
            if (label3.InvokeRequired)
            {
                label3.Invoke(new Action(() => UpdateValidUntilLabel(validUntil)));
            }
            else
            {
                label3.Text = $"检测有效期限：{validUntil:yyyy-MM-dd HH:mm:ss}";
            }
        }



        private DateTime ReadDeadlineFromIni(string iniPath)
        {
            try
            {
                if (!File.Exists(iniPath))
                    return DateTime.MinValue;

                string[] lines = File.ReadAllLines(iniPath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("Deadline="))
                    {
                        string deadlineStr = line.Split('=')[1].Trim();
                        if (DateTime.TryParse(deadlineStr, out DateTime deadline))
                        {
                            return deadline;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取系统文件失败: {ex.Message}", "错误", MessageBoxButtons.OK);
            }

            return DateTime.MinValue;
        }


        private async void Form1_Load(object sender, EventArgs e)
        {
            // 设置默认提示文本
            textBox2.Text = "请输入批次号";
            textBox2.ForeColor = Color.Gray;  // 设置提示文本颜色
            InitializeTextBoxEvents();

            string iniPath = "C:\\system\\system.ini";
            DateTime deadline = ReadDeadlineFromIni(iniPath);
            if (deadline != DateTime.MinValue)
            {
                UpdateValidUntilLabel(deadline);
            }

            Task.Run(() => CheckDeadline()); // 启动检查任务

            try
            {

                // 连接 PLC
                bool plcConnected = await _plcClient.ConnectAsync();
                if (plcConnected)
                {
                    Console.WriteLine("PLC 连接成功");
                    //heartbeatTimer.Start();
                }
                else
                {
                    Console.WriteLine("PLC 连接失败");
                }


                // 连接扫码枪
                string scannerIp = "127.0.0.1"; // 你的扫码枪 IP
                int scannerPort = 5001; // 端口号
                string deviceId = "Scanner_01"; // 设备 ID
                string errorMessage = string.Empty;
                bool scannerConnected = _scanGangBasic.Connect(scannerIp, scannerPort, deviceId, out errorMessage);
                if (scannerConnected)
                {
                    Console.WriteLine("扫码枪连接成功");
                }
                else
                {
                    Console.WriteLine("扫码枪连接失败");
                }

                // 启动 TCP 服务器
                await _tcpServer.StartWoLiuAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化失败: {ex.Message}");
            }
        }


        private async void CheckDeadline()
        {
            while (true)
            {
                string iniPath = "C:\\system\\system.ini";
                DateTime deadline = ReadDeadlineFromIni(iniPath);
                DateTime now = DateTime.Now;

                if (deadline != DateTime.MinValue)
                {
                    TimeSpan remaining = deadline - now;

                    if (remaining.TotalMinutes <= 60 && remaining.TotalMinutes > 59)
                    {
                        //MessageBox.Show("检测有效期即将到期！剩余不到 1 小时。", "提醒", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        UpdateLabel7("提示信息：检测有效期剩余不到1小时");
                    }
                    else if (remaining.TotalMinutes > 60)
                    {

                        UpdateLabel7("");
                    }
                    else if (remaining.TotalSeconds <= 0)
                    {
                        //MessageBox.Show("检测有效期已过期！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        UpdateLabel7("报警信息：检测有效期已过期");
                        // 使用 Invoke 确保 UI 线程操作
                        if (button7.InvokeRequired&& button8.InvokeRequired)
                        {
                            button7.Invoke(new Action(() => button7.Enabled = false));
                            button8.Invoke(new Action(() => button8.Enabled = false));
                        }
                        else
                        {
                            button7.Enabled = false;
                            button8.Enabled = false;
                        }
                    }
                    else
                    {

                        UpdateLabel7("");
                    }
                }

                await Task.Delay(1800000); // 每 30fz检查一次
            }
        }

        /*  private void UpdateLabel7(string text)
          {
              if (label7.InvokeRequired)
              {
                  label7.Invoke(new Action(() => label7.Text = text));
              }
              else
              {
                  label7.Text = text;
              }
          }*/
        private void UpdateLabel7(string text)
        {
            if (label7.InvokeRequired)
            {
                label7.Invoke(new Action(() =>
                {
                    label7.Text = text;
                    label7.Visible = !string.IsNullOrWhiteSpace(text); // 为空时隐藏
                }));
            }
            else
            {
                label7.Text = text;
                label7.Visible = !string.IsNullOrWhiteSpace(text); // 为空时隐藏
            }
        }


        private void UpdateCycleLabel()
        {
            // 更新当前循环次数和总循环次数
            if (label2.InvokeRequired)
            {
                // 如果在非UI线程，使用Invoke来回到UI线程更新
                label2.Invoke(new Action(UpdateCycleLabel));
            }
            else
            {
                label2.Text = $"当前循环次数: {currentCycle}";
            }
        }


        private void StopCalibration(bool isManualStop = false)
        {
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }

            bool isCalibrationSuccessful = (currentCycle > 0 && currentCycle >= totalCycles);

            currentCycle = 0;
            totalCycles = 0;
            isOn1 = false;
            button5.Enabled = true;
            button6.Enabled = false;
            string stopTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // 写入系统文件 Deadline 部分
            WriteStopTimeToFile(stopTime);
            // WriteDeadlineToIni(iniPath,stopTime);
            this.Invoke(new Action(() =>
            {
                label6.Text = "自校准模式";
                label1.Text = "当前状态：待机状态";
                label2.Text = "当前循环次数：0";

                // 手动停止 or 异常终止，都应该禁用检测模式
                button7.Enabled = isCalibrationSuccessful && !isManualStop;
                button8.Enabled = isCalibrationSuccessful && !isManualStop;
                label3.Text = $"停止自校准时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            }));
        }

        private void WriteStopTimeToFile(string stopTime)
        {
            string iniPath = "C:\\system\\system.ini"; // 系统文件路径

            try
            {
                // 读取文件内容
                var lines = File.ReadAllLines(iniPath).ToList();

                bool deadlineUpdated = false;

                // 遍历文件中的每一行，查找 "Deadline" 字段并更新它
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("Deadline="))
                    {
                        lines[i] = $"Deadline={stopTime}";  // 更新 Deadline 值
                        deadlineUpdated = true;
                        break;  // 找到并更新后退出循环
                    }
                }

                // 如果没有找到 Deadline 字段，添加一个新的字段
                if (!deadlineUpdated)
                {
                    lines.Add($"Deadline={stopTime}");
                }

                // 将更新后的内容写回到文件
                File.WriteAllLines(iniPath, lines);
            }
            catch (Exception ex)
            {
                // 如果写入失败，显示错误信息
                MessageBox.Show($"写入系统文件失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripComboBox1_Click(object sender, EventArgs e)
        {

        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }



        /* private void Form1_Load(object sender, EventArgs e)
         {

         }*/


        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }





        private void label4_Click_1(object sender, EventArgs e)
        {

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }

        private void button4_Click(object sender, EventArgs e)
        {

            if (!isOn)
            {
                MessageBox.Show("请先进入检测模式！", "提示", MessageBoxButtons.OK);
                return;
            }


            string batchNumber = textBox2.Text.Trim();
            BatchNumber = Encoding.UTF8.GetBytes(batchNumber);
            detectionTimer.Start();

            if (string.IsNullOrWhiteSpace(batchNumber))
            {
                MessageBox.Show("批次号不能为空，请输入有效的批次号！", "警告", MessageBoxButtons.OK);
                return;
            }


            SaveBatchNumberToFile(batchNumber);
        }
        private void SaveBatchNumberToFile(string batchNumber)
        {
            string directoryPath = @"C:\system\"; // 保存目录
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string filePath = Path.Combine(directoryPath, $"{timestamp}_batch.txt");

            try
            {

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }


                string content = $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n批次号：{batchNumber}\n-------------------\n";
                File.AppendAllText(filePath, content, Encoding.UTF8);


                MessageBox.Show($"批次号已成功保存到文件：{filePath}", "保存成功", MessageBoxButtons.OK);
            }
            catch (Exception ex)
            {

                MessageBox.Show($"保存批次号时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK);
            }
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }



        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void label7_Click(object sender, EventArgs e)
        {

        }

        private void label9_Click(object sender, EventArgs e)
        {

        }

        private async void button7_Click_1(object sender, EventArgs e)
        {
            bool writeSuccess = await _plcClient.WriteDRegisterAsync(2130, 1);

            if (writeSuccess)
            {
                isOn = true;

                button8.Enabled = true;   // 启用退出按钮

                // button2.Text = "进入检测模式"; // 这个可以根据需要修改按钮文本
                label9.Text = "检测模式运行中";

                label1.Text = "当前状态：检测模式";

                button5.Enabled = false;  // 禁用其他不需要的按钮
                button6.Enabled = false;
                textBox2.Enabled = true;
                button4.Enabled = true;


               

                UpdateLabel7(""); // 清空报警信息
            }
            else
            {
                // MessageBox.Show("无法进入检测模式，PLC通信失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateLabel7("报警信息：无法进入检测模式！");
            }
        }

        private async void button8_Click(object sender, EventArgs e)
        {
            bool writeSuccess = await _plcClient.WriteDRegisterAsync(2140, 1);

            if (writeSuccess)
            {
                isOn = false;
                detectionTimer.Stop();
                button7.Enabled = true;
                button8.Enabled = true;

                label9.Text = "检测模式";  // 修改按钮文本
                label1.Text = "当前状态：待机状态";

                button5.Enabled = true;
                button6.Enabled = true;
                textBox2.Enabled = false;
                button4.Enabled = false;
               
                UpdateLabel7("");
            }
            else
            {
                //MessageBox.Show("无法退出检测模式，PLC通信失败！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateLabel7("无法退出检测模式！");
            }
        }

        // 处理文本框获取焦点事件
        private void textBox2_Enter(object sender, EventArgs e)
        {
            if (textBox2.Text == "请输入批次号")
            {
                textBox2.Text = "";
                textBox2.ForeColor = Color.Black;  // 设置正常输入的颜色
            }
        }

        // 处理文本框失去焦点事件
        private void textBox2_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textBox2.Text))
            {
                textBox2.Text = "请输入批次号";
                textBox2.ForeColor = Color.Gray;  // 恢复提示文本颜色
            }
        }

        // 绑定事件
        private void InitializeTextBoxEvents()
        {
            textBox2.Enter += textBox2_Enter;
            textBox2.Leave += textBox2_Leave;
        }
    }


}
