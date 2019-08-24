using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace InhouseCamguard
{
    public static class FileHelpers
    {
        private const string AppName = "Inhouse Camguard";
        public const string CsvExtension = ".csv";

        public static string GetDefaultDirectory()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppName, "DataLogs");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetDefaultFilename()
        {
            return "Log" + DateTime.Now.ToString("_yyMMdd-hhmmss");
        }

        public static string EnsureUnique(string filename)
        {
            string dir = Path.GetDirectoryName(filename);
            string fn = Path.GetFileNameWithoutExtension(filename);
            if (Regex.IsMatch(fn, @"_\d{6}-\d{6}"))
                fn = Regex.Replace(fn, @"_\d{6}-\d{6}", DateTime.Now.ToString("_yyMMdd-hhmmss"));
            else
                fn = fn + DateTime.Now.ToString("_yyMMdd-hhmmss");

            return Path.Combine(dir, fn + CsvExtension);
        }

        public static string GetDefaultImageDirectory()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppName, "Images");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static string BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }
    }
}