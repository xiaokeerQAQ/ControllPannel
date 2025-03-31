using BLL.Hardware.ScanGang;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinFormsApp1321
{
    public partial class Form2 : Form
    {
        public string BarcodeText { get; set; }
        private ScanGangBasic _scanner;

        public Form2()
        {
            InitializeComponent();
            _scanner = new ScanGangBasic(); // 初始化扫码枪对象
            textBox2.ReadOnly = true; // 设置 textBox2 为只读
        }

        /// <summary>
        /// 读取条码并显示在 textBox2
        /// </summary>
        public void ReadAndDisplayBarcode()
        {
            string result;
            string errStr;
            bool success = _scanner.ScanOnce(out result, out errStr);

            if (success && result != "未扫描到条码")
            {
                textBox2.Text = result;
            }
            else
            {
                textBox2.Text = string.Empty; // 清空 textBox2
                MessageBox.Show($"扫描失败：{errStr}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 保存pc到文件
        /// </summary>
        public void SaveBarcodeToFile(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
            {
                MessageBox.Show("批次不能为空！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string directoryPath = @"C:\system\";
            string filePath = Path.Combine(directoryPath, $"{timestamp}.txt");

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                string content = $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n批次：{barcode}\n-------------------\n";
                File.AppendAllText(filePath, content, Encoding.UTF8);

                MessageBox.Show($"批次已保存到 {filePath}", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存批次失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 确认按钮（button1）点击事件
        /// </summary>
        private void button1_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textBox2.Text))
            {
                MessageBox.Show("条码不能为空或无效！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            BarcodeText = textBox2.Text.Trim(); // 记录条码
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        /// <summary>
        /// 取消按钮（button2）点击事件
        /// </summary>
        private void button2_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        /// <summary>
        /// 触发扫码枪扫描（button3）点击事件
        /// </summary>


        /*private void button3_Click_1(object sender, EventArgs e)
        {
            string errStr;

            // 1. 发送扫码指令 "S"
            bool commandSent = _scanner.SendCommd("S", out errStr);
            if (!commandSent)
            {
                MessageBox.Show($"扫码指令发送失败: {errStr}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 2. 等待扫码结果
            string result;
            bool success = _scanner.ScanOnce(out result, out errStr);

            if (success && result != "未扫描到条码")
            {
                textBox2.Text = result;
            }
            else
            {
                textBox2.Text = string.Empty; // 清空 textBox2
                MessageBox.Show($"扫描失败: {errStr}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
*/
        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
