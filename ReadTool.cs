using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class ReadTool
{
    private List<string> _fileLines;

    public ReadTool(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("文件未找到: " + filePath);
        }
        _fileLines = File.ReadAllLines(filePath).ToList();
    }

    // 读取字符串并转换为 byte[]
    public byte[] ReadStringAsBytes(string section, string key)
    {
        string value = ReadString(section, key);
        return string.IsNullOrEmpty(value) ? new byte[0] : Encoding.ASCII.GetBytes(value);
    }

    // 读取浮点数并转换为 byte[]
    public byte[] ReadFloatAsBytes(string section, string key)
    {
        float value = ReadFloat(section, key);
        return BitConverter.GetBytes(value);
    }

    // 读取整数并转换为 byte[]
    public byte[] ReadIntAsBytes(string section, string key)
    {
        int value = ReadInt(section, key);
        return BitConverter.GetBytes(value);
    }

    // 读取多个 dX 位置数据并转换为 byte[]
    public byte[] ReadAllDefectPositionsAsBytes(string section)
    {
        List<int> positions = ReadAllDefectPositions(section);
        List<byte> bytes = new List<byte>();
        foreach (var position in positions)
        {
            bytes.AddRange(BitConverter.GetBytes(position));
        }
        return bytes.ToArray();
    }

    // 读取字符串值
    private string ReadString(string section, string key)
    {
        bool inSection = false;
        foreach (var line in _fileLines)
        {
            if (line.StartsWith($"[{section}]"))
            {
                inSection = true;
                continue;
            }

            if (inSection && line.StartsWith(key))
            {
                return line.Split('=')[1].Trim();
            }

            if (line.StartsWith("["))
            {
                inSection = false;
            }
        }
        return string.Empty;
    }

    // 读取浮点数
    private float ReadFloat(string section, string key)
    {
        string value = ReadString(section, key);
        return float.TryParse(value, out float result) ? result : 0.0f;
    }

    // 读取整数
    private int ReadInt(string section, string key)
    {
        string value = ReadString(section, key);
        return int.TryParse(value, out int result) ? result : 0;
    }

    // 读取多个 dX 位置（动态解析 d1, d2, d3...）
    private List<int> ReadAllDefectPositions(string section)
    {
        bool inSection = false;
        List<int> positions = new List<int>();

        foreach (var line in _fileLines)
        {
            if (line.StartsWith($"[{section}]"))
            {
                inSection = true;
                continue;
            }

            if (inSection)
            {
                if (line.StartsWith("["))
                {
                    break; // 遇到新 section 退出
                }

                string[] parts = line.Split('=');
                if (parts.Length == 2 && parts[0].Trim().StartsWith("d"))
                {
                    if (int.TryParse(parts[1].Trim(), out int pos))
                    {
                        positions.Add(pos);
                    }
                }
            }
        }
        return positions;
    }
}
