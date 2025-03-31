using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinFormsApp1321
{
    public static class ConfigDataStore
    {
        public static byte[] CodeBytes { get; set; }
        public static byte[] ToleranceBytes { get; set; }
        public static byte[] CountBytes { get; set; }
        public static byte[] DefectPositionsBytes { get; set; }
    }
}