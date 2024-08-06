using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocImageDownload
{
    public static class FFMpeg
    {
        static string ffmpegPath = "";
        public static void Init(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("ffmpeg.exe not found");
            }
            ffmpegPath = path;
        }
        static readonly string TempFileName = "temp.avif";
        public static async Task<bool> Convert(string inputFilePath, string outputFilePath, bool CompareSizes = true)
        {
            if (string.IsNullOrEmpty(inputFilePath))
            {
                return false;
            }
            if (File.Exists(TempFileName))
            {
                File.Delete(TempFileName);
            }
            ProcessStartInfo startInfo = new()
            {
                FileName = ffmpegPath,
                Arguments = $"-y -i {inputFilePath} -c:v libaom-av1 {TempFileName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = new()
            {
                StartInfo = startInfo
            };

            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(outputTask, errorTask);
            if (!CompareSizes)
            {
                File.Move(TempFileName, outputFilePath, true);
                return true;
            }
            FileInfo inputFileInfo = new(inputFilePath);
            FileInfo outputFileInfo = new(TempFileName);
            if (inputFileInfo.Length <= outputFileInfo.Length)
            {
                File.Delete(TempFileName);
                return true;
            }
            File.Move(TempFileName, outputFilePath, true);
            //string output = outputTask.Result;
            //string error = errorTask.Result;

            //Console.WriteLine("Output: " + output);
            //Console.WriteLine("Error: " + error);
            return true;
        }
    }
}
